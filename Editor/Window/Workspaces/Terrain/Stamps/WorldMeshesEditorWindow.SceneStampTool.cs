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
            "The Scene Stamp Tool provides direct selection, XZ movement, " +
            "footprint resizing, and Height Delta editing for terrain " +
            "stamp modifiers. Scene edits use the same StableId selection " +
            "and TerrainAuthoringModifierService mutation boundary as the " +
            "production modifier inspector.",
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

        GUILayout.Space(4f);

        GUILayout.Label(
            "Scene Overlays",
            EditorStyles.miniBoldLabel
        );

        bool showFalloff =
            EditorGUILayout.Toggle(
                "Falloff Visualization",
                TerrainStampEditorToolPreferences
                    .ShowFalloffVisualization
            );

        if (
            showFalloff !=
            TerrainStampEditorToolPreferences
                .ShowFalloffVisualization
        )
        {
            TerrainStampEditorToolPreferences
                .ShowFalloffVisualization =
                    showFalloff;
        }

        bool showAffectedTiles =
            EditorGUILayout.Toggle(
                "Affected Tile Overlay",
                TerrainStampEditorToolPreferences
                    .ShowAffectedTileOverlay
            );

        if (
            showAffectedTiles !=
            TerrainStampEditorToolPreferences
                .ShowAffectedTileOverlay
        )
        {
            TerrainStampEditorToolPreferences
                .ShowAffectedTileOverlay =
                    showAffectedTiles;
        }

        EditorGUILayout.HelpBox(
            "Falloff Visualization shows the selected stamp's exact " +
            "full-strength boundary. Affected Tile Overlay shows the " +
            "height tiles that the existing dirty-region utility considers " +
            "affected, including its normal one-sample padding.",
            MessageType.None
        );

        bool editingAllowed =
            contextAvailable
            &&
            !EditorApplication.isPlaying
            &&
            !EditorApplication
                .isPlayingOrWillChangePlaymode;

        GUILayout.Space(4f);

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
                "Click a footprint to select it; repeated clicks at the same " +
                "location cycle overlapping stamps. Drag the larger center " +
                "handle to move in X/Z. Drag edge or corner handles to resize. " +
                "Hold Alt while resizing to resize symmetrically around the " +
                "stamp center. Hold Shift while dragging a corner to preserve " +
                "the stamp's starting aspect ratio; Alt + Shift combines both. " +
                "When Falloff Visualization is enabled, drag the small handles " +
                "on the inner full-strength rectangle to edit Falloff. Use the " +
                "vertical ΔH ruler beside the selected stamp to edit Height " +
                "Delta. Each complete drag is one Undo operation. Press Escape " +
                "during a drag to cancel it. Press F to frame the selected " +
                "modifier.",
                MessageType.Info
            );
        }

        GUILayout.EndVertical();
    }
}
