using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showSceneStampEditingHelp;

    /*
     * Package 5 authoritative Scene-edit UI.
     *
     * TerrainStampEditorTool itself is unchanged. This toolbar only moves the
     * existing activation/deactivation and overlay preferences to the top of
     * the Terrain authoring workflow.
     */
    private void DrawStampAuthoringToolbarSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.BeginHorizontal();

        GUILayout.Label(
            "Stamp Authoring",
            EditorStyles.boldLabel
        );

        GUILayout.FlexibleSpace();

        bool toolActive =
            ToolManager.activeToolType ==
                typeof(TerrainStampEditorTool);

        GUILayout.Label(
            toolActive
                ? "Scene Editing Active"
                : "Scene Editing Inactive",
            EditorStyles.miniLabel
        );

        GUILayout.EndHorizontal();

        bool contextAvailable =
            TerrainAuthoringModifierContextUtility
                .TryLoadDefault(
                    out _,
                    out _,
                    out string contextError
                );

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
                    ? "Exit Scene Editing"
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

        GUILayout.Space(
            4f
        );

        GUILayout.BeginHorizontal();

        bool showFalloff =
            GUILayout.Toggle(
                TerrainStampEditorToolPreferences
                    .ShowFalloffVisualization,
                new GUIContent(
                    "Falloff Overlay",
                    "Show the selected stamp's exact full-strength falloff boundary in the Scene View."
                ),
                GUILayout.ExpandWidth(true)
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
            GUILayout.Toggle(
                TerrainStampEditorToolPreferences
                    .ShowAffectedTileOverlay,
                new GUIContent(
                    "Affected Tiles",
                    "Show height tiles affected by the selected stamp, including the normal dirty-region padding."
                ),
                GUILayout.ExpandWidth(true)
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

        GUILayout.EndHorizontal();

        if (!contextAvailable)
        {
            EditorGUILayout.HelpBox(
                contextError,
                MessageType.Warning
            );
        }
        else if (toolActive)
        {
            GUILayout.Space(
                3f
            );

            EditorGUILayout.LabelField(
                "Drag handles to edit. Escape cancels the active drag; F frames the selected stamp.",
                EditorStyles.wordWrappedLabel
            );
        }

        showSceneStampEditingHelp =
            EditorGUILayout.Foldout(
                showSceneStampEditingHelp,
                "Scene Editing Help",
                true
            );

        if (showSceneStampEditingHelp)
        {
            EditorGUILayout.HelpBox(
                "Click a footprint to select it; repeated clicks cycle overlapping stamps. " +
                "Drag the center handle to move in X/Z, edge/corner handles to resize, " +
                "the rotation handle to rotate, falloff handles to edit Falloff, and the " +
                "vertical ΔH ruler to edit Height Delta. Hold Alt while resizing for " +
                "symmetric resizing and Shift on a corner to preserve the starting aspect " +
                "ratio. Each complete drag is one Undo operation. Escape cancels the active " +
                "drag and F frames the selected modifier.",
                MessageType.Info
            );
        }

        GUILayout.EndVertical();
    }
}
