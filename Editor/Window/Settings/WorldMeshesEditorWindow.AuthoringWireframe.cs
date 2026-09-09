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
            "Preview Assets",
            TerrainAuthoringWireframeSectionCache
                .PreviewAssetStatusLabel
        );

        EditorGUILayout.LabelField(
            "Source Renderers",
            TerrainAuthoringWireframeRenderer
                .SourceRendererCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Generated Wireframe Sections",
            TerrainAuthoringWireframeSectionCache
                .TotalSectionDescriptorCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Generated Wire Edges",
            TerrainAuthoringWireframeRenderer
                .CachedEdgeCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Rendered Sections",
            TerrainAuthoringWireframeCulling
                .RenderedSectionCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Rendered Wire Edges",
            TerrainAuthoringWireframeCulling
                .RenderedEdgeCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "LOD-Culled Sections",
            TerrainAuthoringWireframeCulling
                .LODCulledSectionCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Distance-Culled Sections",
            TerrainAuthoringWireframeCulling
                .DistanceCulledSectionCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Frustum-Culled Sections",
            TerrainAuthoringWireframeCulling
                .FrustumCulledSectionCount
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

            "Wireframe preview geometry is generated once alongside the " +
            "clipmap meshes. Enabling or disabling Wireframe only changes " +
            "editor visualization; it does not rebuild triangle topology " +
            "or edge buffers.\n\n" +

            "Overlay draws the generated explicit MeshTopology.Lines " +
            "sections over the normal terrain. Wireframe Only suppresses " +
            "the normal fill, renders each section's displaced triangle " +
            "submesh as an invisible depth proxy, then renders its exact " +
            "deduplicated line submesh.\n\n" +

            "Terrain height/stamp changes do not require preview " +
            "regeneration because displacement still comes from the " +
            "current height cache. Clipmap topology changes require the " +
            "normal Generate / Regenerate Clipmap Meshes operation.\n\n" +

            "These preview assets and controls are editor-only and do not " +
            "alter runtime terrain, LOD layout, heightmap streaming, or " +
            "collision streaming.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
