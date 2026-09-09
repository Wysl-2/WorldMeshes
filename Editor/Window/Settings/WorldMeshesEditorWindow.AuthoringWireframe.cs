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
            "Section Descriptors",
            TerrainAuthoringWireframeSectionCache
                .TotalSectionDescriptorCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Built Proxy Sections",
            TerrainAuthoringWireframeSectionCache
                .BuiltSectionCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Unbuilt / Lazy Sections",
            TerrainAuthoringWireframeSectionCache
                .UnbuiltSectionCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Pending Section Builds",
            TerrainAuthoringWireframeSectionCache
                .PendingBuildCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Cached Wire Edges",
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

        GUILayout.Space(
            5f
        );

        EditorGUILayout.LabelField(
            "Wire Representation",
            TerrainAuthoringWireframeSectionCache
                .ActiveRepresentationLabel
        );

        EditorGUILayout.LabelField(
            "First Visible Latency",
            FormatWireframeMilliseconds(
                TerrainAuthoringWireframeSectionCache
                    .FirstVisibleLatencyMilliseconds,
                true
            )
        );

        EditorGUILayout.LabelField(
            "Last Source Preparation",
            FormatWireframeMilliseconds(
                TerrainAuthoringWireframeSectionCache
                    .LastSourcePreparationMilliseconds
            )
        );

        EditorGUILayout.LabelField(
            "Last / Avg Section Build",
            FormatWireframeMilliseconds(
                TerrainAuthoringWireframeSectionCache
                    .LastSectionBuildMilliseconds
            ) +
            " / " +
            FormatWireframeMilliseconds(
                TerrainAuthoringWireframeSectionCache
                    .AverageSectionBuildMilliseconds
            )
        );

        EditorGUILayout.LabelField(
            "Last Vertex Preparation",
            FormatWireframeMilliseconds(
                TerrainAuthoringWireframeSectionCache
                    .LastVertexPreparationMilliseconds
            )
        );

        EditorGUILayout.LabelField(
            "Last Mesh Upload",
            FormatWireframeMilliseconds(
                TerrainAuthoringWireframeSectionCache
                    .LastMeshUploadMilliseconds
            )
        );

        EditorGUILayout.LabelField(
            "Depth / Wire Submission",
            FormatWireframeMilliseconds(
                TerrainAuthoringWireframeSectionCache
                    .LastDepthSubmissionMilliseconds
            ) +
            " / " +
            FormatWireframeMilliseconds(
                TerrainAuthoringWireframeSectionCache
                    .LastWireSubmissionMilliseconds
            )
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

            "Package 3 keeps Package 2 spatial descriptors, lazy builds, " +
            "caching, LOD limits, distance culling, and frustum culling, " +
            "but replaces explicit MeshTopology.Lines proxies with one " +
            "barycentric triangle mesh per built section.\n\n" +

            "Overlay draws barycentric displaced triangle edges over the " +
            "normal terrain. Wireframe Only reuses the same section mesh " +
            "first as a solid depth proxy and then as visible wire, so " +
            "hidden terrain edges remain occluded.\n\n" +

            "Cached / Rendered Wire Edges now count triangle-edge " +
            "incidences represented by the barycentric meshes; shared " +
            "triangle edges are therefore counted from both triangles.\n\n" +

            "All controls and diagnostics remain editor-only and do not " +
            "change generated clipmap geometry, runtime terrain, LOD " +
            "layout, heightmap streaming, or collision streaming.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }

    private static string FormatWireframeMilliseconds(
        double milliseconds,
        bool pendingWhenNegative = false
    )
    {
        if (
            pendingWhenNegative
            &&
            milliseconds < 0.0
        )
        {
            return
                "Pending";
        }

        return
            Mathf.Max(
                0f,
                (float)milliseconds
            )
            .ToString(
                "0.00"
            ) +
            " ms";
    }
}
