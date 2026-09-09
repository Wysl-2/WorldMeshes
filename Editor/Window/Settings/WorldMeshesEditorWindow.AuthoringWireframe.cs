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

        int maximumLOD =
            TerrainAuthoringWireframeCulling
                .MaximumLOD;

        EditorGUI.BeginChangeCheck();

        int newMaximumLOD =
            EditorGUILayout.IntField(
                "Maximum Wireframe LOD",
                maximumLOD
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringWireframeCulling
                .MaximumLOD =
                    newMaximumLOD;
        }

        bool distanceLimitEnabled =
            TerrainAuthoringWireframeCulling
                .DistanceLimitEnabled;

        EditorGUI.BeginChangeCheck();

        bool newDistanceLimitEnabled =
            EditorGUILayout.Toggle(
                "Limit Wireframe Distance",
                distanceLimitEnabled
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringWireframeCulling
                .DistanceLimitEnabled =
                    newDistanceLimitEnabled;

            distanceLimitEnabled =
                newDistanceLimitEnabled;
        }

        EditorGUI.BeginDisabledGroup(
            !distanceLimitEnabled
        );

        float maximumDistance =
            TerrainAuthoringWireframeCulling
                .MaximumDistance;

        EditorGUI.BeginChangeCheck();

        float newMaximumDistance =
            EditorGUILayout.FloatField(
                "Maximum Wireframe Distance",
                maximumDistance
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringWireframeCulling
                .MaximumDistance =
                    newMaximumDistance;
        }

        EditorGUI.EndDisabledGroup();

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
            "Cached Wire Edges",
            TerrainAuthoringWireframeRenderer
                .CachedEdgeCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Rendered Proxy Meshes",
            TerrainAuthoringWireframeCulling
                .RenderedProxyMeshCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Rendered Wire Edges",
            TerrainAuthoringWireframeCulling
                .RenderedEdgeCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "LOD-Culled Proxies",
            TerrainAuthoringWireframeCulling
                .LODCulledProxyCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Distance-Culled Proxies",
            TerrainAuthoringWireframeCulling
                .DistanceCulledProxyCount
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
            "conservative renderer bounds.\n\n" +

            "Maximum Wireframe LOD and Maximum Wireframe Distance " +
            "only control editor Scene View proxy submission. They do " +
            "not change generated clipmap geometry, runtime terrain, " +
            "LOD layout, heightmap streaming, or collision streaming. " +
            "Disable the distance limit to preserve unrestricted " +
            "distance rendering.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
