using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawAuthoringWireframeSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "True Displaced Wireframe",
            EditorStyles.boldLabel
        );

        bool enabled =
            TerrainAuthoringWireframeRenderer
                .WireframeEnabled;

        EditorGUI.BeginChangeCheck();

        bool newEnabled =
            EditorGUILayout.Toggle(
                "Wireframe",
                enabled
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringWireframeRenderer
                .WireframeEnabled =
                    newEnabled;

            enabled =
                newEnabled;
        }

        EditorGUI.BeginDisabledGroup(
            !enabled
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        TerrainAuthoringWireframeMode mode =
            TerrainAuthoringWireframeRenderer
                .Mode;

        EditorGUI.BeginChangeCheck();

        TerrainAuthoringWireframeMode newMode =
            (TerrainAuthoringWireframeMode)
            EditorGUILayout.EnumPopup(
                "Mode",
                mode
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringWireframeRenderer
                .Mode =
                    newMode;
        }

        Color wireframeColor =
            TerrainAuthoringWireframeRenderer
                .WireframeColor;

        EditorGUI.BeginChangeCheck();

        Color newWireframeColor =
            EditorGUILayout.ColorField(
                "Wireframe Color",
                wireframeColor
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringWireframeRenderer
                .WireframeColor =
                    newWireframeColor;
        }

        float opacity =
            TerrainAuthoringWireframeRenderer
                .WireframeOpacity;

        EditorGUI.BeginChangeCheck();

        float newOpacity =
            EditorGUILayout.Slider(
                "Wireframe Opacity",
                opacity,
                0f,
                1f
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringWireframeRenderer
                .WireframeOpacity =
                    newOpacity;
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.LabelField(
            "Status",
            TerrainAuthoringWireframeRenderer
                .StatusLabel
        );

        EditorGUILayout.LabelField(
            "Source Renderers",
            TerrainAuthoringWireframeRenderer
                .SourceRendererCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Cached Proxy Meshes",
            TerrainAuthoringWireframeRenderer
                .CachedProxyMeshCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Unique Wire Edges",
            TerrainAuthoringWireframeRenderer
                .CachedEdgeCount
                .ToString()
        );

        string statusMessage =
            TerrainAuthoringWireframeRenderer
                .StatusMessage;

        if (
            !string.IsNullOrEmpty(
                statusMessage
            )
        )
        {
            MessageType messageType =
                TerrainAuthoringWireframeRenderer.Status ==
                    TerrainAuthoringWireframeStatus.Error
                    ||
                    TerrainAuthoringWireframeRenderer.Status ==
                        TerrainAuthoringWireframeStatus.ShaderUnavailable
                    ? MessageType.Error
                    :
                    TerrainAuthoringWireframeRenderer.Status ==
                        TerrainAuthoringWireframeStatus.ClipmapUnavailable
                    ||
                    TerrainAuthoringWireframeRenderer.Status ==
                        TerrainAuthoringWireframeStatus.PlayMode
                        ? MessageType.Warning
                        : MessageType.Info;

            EditorGUILayout.HelpBox(
                statusMessage,
                messageType
            );
        }

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "This is a custom displaced wireframe, not Unity's " +
            "built-in Scene View wireframe mode.\n\n" +

            "Overlay keeps the normal Stage 5 terrain visible and " +
            "draws the displaced triangle edges on top.\n\n" +

            "Wireframe Only suppresses the normal terrain fill and " +
            "draws an invisible displaced depth proxy before the " +
            "lines so terrain still occludes hidden edges.\n\n" +

            "Proxy meshes are transient editor resources. They copy " +
            "the generated clipmap positions and TEXCOORD3 stitch " +
            "weights, then reuse the source renderer's current height " +
            "cache, stitch offset, world bounds, transform, and " +
            "conservative renderer bounds.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
