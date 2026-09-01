using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawTerrainStampSceneToolSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Scene Stamp Tool",
            EditorStyles.boldLabel
        );

        bool toolActive =
            ToolManager.activeToolType ==
                typeof(TerrainStampEditorTool);

        EditorGUILayout.LabelField(
            "Status",
            toolActive
                ? "Active"
                : "Inactive"
        );

        EditorGUILayout.HelpBox(
            "The Scene Stamp Tool draws terrain stamp footprints and " +
            "provides direct XZ move and resize handles. Scene edits " +
            "use the same StableId selection and modifier service as " +
            "the production modifier inspector.",
            MessageType.None
        );

        bool contextAvailable =
            TerrainAuthoringModifierContextUtility
                .TryLoadDefault(
                    out _,
                    out _,
                    out string contextError
                );

        if (!contextAvailable)
        {
            EditorGUILayout.HelpBox(
                contextError,
                MessageType.Warning
            );
        }

        bool editingAllowed =
            contextAvailable
            &&
            !EditorApplication.isPlaying
            &&
            !EditorApplication
                .isPlayingOrWillChangePlaymode;

        EditorGUI.BeginDisabledGroup(
            !toolActive
            &&
            !editingAllowed
        );

        if (
            GUILayout.Button(
                toolActive
                    ? "Exit Scene Stamp Tool"
                    : "Edit Stamps In Scene View",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            if (toolActive)
            {
                ToolManager
                    .RestorePreviousPersistentTool();
            }
            else
            {
                ToolManager
                    .SetActiveTool<TerrainStampEditorTool>();

                if (
                    SceneView.lastActiveSceneView !=
                        null
                )
                {
                    SceneView.lastActiveSceneView
                        .Focus();
                }
            }

            SceneView.RepaintAll();
            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        if (toolActive)
        {
            GUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                "Click a footprint to select it. Drag the center handle " +
                "to move in X/Z. Drag edge or corner handles to resize. " +
                "Each complete drag is one Undo operation. Press Escape " +
                "during a drag to cancel it.",
                MessageType.Info
            );
        }

        GUILayout.EndVertical();
    }
}
