using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawAuthoringVisualizationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Authoring Visualization",
            EditorStyles.boldLabel
        );

        // =================================================
        // BASE MODE
        // =================================================

        TerrainAuthoringVisualizationMode baseMode =
            TerrainAuthoringVisualizationController
                .BaseMode;

        EditorGUI.BeginChangeCheck();

        TerrainAuthoringVisualizationMode newBaseMode =
            (TerrainAuthoringVisualizationMode)
            EditorGUILayout.EnumPopup(
                "Base Mode",
                baseMode
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringVisualizationController
                .BaseMode =
                    newBaseMode;

            baseMode =
                newBaseMode;
        }

        // =================================================
        // CURVATURE
        // =================================================

        if (
            baseMode ==
                TerrainAuthoringVisualizationMode.Curvature
        )
        {
            GUILayout.Space(
                5f
            );

            GUILayout.Label(
                "Curvature",
                EditorStyles.boldLabel
            );

            float curvatureScale =
                TerrainAuthoringVisualizationController
                    .CurvatureScale;

            EditorGUI.BeginChangeCheck();

            float newCurvatureScale =
                EditorGUILayout.Slider(
                    "Scale (m)",
                    curvatureScale,
                    1f,
                    256f
                );

            if (EditorGUI.EndChangeCheck())
            {
                TerrainAuthoringVisualizationController
                    .CurvatureScale =
                        newCurvatureScale;
            }

            GUILayout.BeginHorizontal();

            GUILayout.Label(
                "Presets",
                GUILayout.Width(
                    100f
                )
            );

            DrawCurvatureScalePresetButton(
                2f
            );

            DrawCurvatureScalePresetButton(
                8f
            );

            DrawCurvatureScalePresetButton(
                16f
            );

            DrawCurvatureScalePresetButton(
                32f
            );

            DrawCurvatureScalePresetButton(
                64f
            );

            GUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Scale is the world-space sampling radius used to " +
                "measure terrain curvature. Small values reveal local " +
                "surface detail; larger values emphasize broader ridges, " +
                "shoulders, and gullies.",
                MessageType.None
            );
        }

        GUILayout.Space(
            5f
        );

        GUILayout.Label(
            "Overlays",
            EditorStyles.boldLabel
        );

        // =================================================
        // CONTOURS
        // =================================================

        bool contoursEnabled =
            TerrainAuthoringVisualizationController
                .ContoursEnabled;

        EditorGUI.BeginChangeCheck();

        bool newContoursEnabled =
            EditorGUILayout.Toggle(
                "Contours",
                contoursEnabled
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringVisualizationController
                .ContoursEnabled =
                    newContoursEnabled;

            contoursEnabled =
                newContoursEnabled;
        }

        EditorGUI.BeginDisabledGroup(
            !contoursEnabled
        );

        float contourInterval =
            TerrainAuthoringVisualizationController
                .ContourInterval;

        EditorGUI.BeginChangeCheck();

        float newContourInterval =
            EditorGUILayout.FloatField(
                "Contour Interval (m)",
                contourInterval
            );

        if (EditorGUI.EndChangeCheck())
        {
            TerrainAuthoringVisualizationController
                .ContourInterval =
                    newContourInterval;
        }

        GUILayout.BeginHorizontal();

        GUILayout.Label(
            "Presets",
            GUILayout.Width(
                100f
            )
        );

        DrawContourPresetButton(
            5f
        );

        DrawContourPresetButton(
            10f
        );

        DrawContourPresetButton(
            20f
        );

        DrawContourPresetButton(
            50f
        );

        GUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();

        // =================================================
        // SPATIAL OVERLAYS
        // =================================================

        DrawVisualizationToggle(
            "Chunk Grid",
            TerrainAuthoringVisualizationController
                .ChunkGridEnabled,
            value =>
                TerrainAuthoringVisualizationController
                    .ChunkGridEnabled =
                        value
        );

        DrawVisualizationToggle(
            "Height Tile Grid",
            TerrainAuthoringVisualizationController
                .HeightTileGridEnabled,
            value =>
                TerrainAuthoringVisualizationController
                    .HeightTileGridEnabled =
                        value
        );

        DrawVisualizationToggle(
            "World Boundary",
            TerrainAuthoringVisualizationController
                .WorldBoundaryEnabled,
            value =>
                TerrainAuthoringVisualizationController
                    .WorldBoundaryEnabled =
                        value
        );

        DrawVisualizationToggle(
            "LOD Regions",
            TerrainAuthoringVisualizationController
                .LODRegionsEnabled,
            value =>
                TerrainAuthoringVisualizationController
                    .LODRegionsEnabled =
                        value
        );

        // =================================================
        // STATUS
        // =================================================

        GUILayout.Space(
            5f
        );

        EditorGUILayout.LabelField(
            "Status",
            TerrainAuthoringVisualizationController
                .StatusLabel
        );

        EditorGUILayout.LabelField(
            "Bound Renderers",
            TerrainAuthoringVisualizationController
                .BoundRendererCount
                .ToString()
        );

        string statusMessage =
            TerrainAuthoringVisualizationController
                .StatusMessage;

        if (
            !string.IsNullOrEmpty(
                statusMessage
            )
        )
        {
            MessageType messageType =
                TerrainAuthoringVisualizationController.Status ==
                    TerrainAuthoringVisualizationStatus.Error
                    ? MessageType.Error
                    :
                    TerrainAuthoringVisualizationController.Status ==
                        TerrainAuthoringVisualizationStatus.HeightPreviewRequired
                    ||
                    TerrainAuthoringVisualizationController.Status ==
                        TerrainAuthoringVisualizationStatus.ClipmapUnavailable
                    ||
                    TerrainAuthoringVisualizationController.Status ==
                        TerrainAuthoringVisualizationStatus.PlayMode
                        ? MessageType.Warning
                        : MessageType.Info;

            EditorGUILayout.HelpBox(
                statusMessage,
                messageType
            );
        }

        // =================================================
        // RESET
        // =================================================

        GUILayout.Space(
            5f
        );

        EditorGUI.BeginDisabledGroup(
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Reset Visualization",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainAuthoringVisualizationController
                .ResetVisualization();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Lit uses the normal terrain PBR path. Height, Slope, and " +
            "Curvature are unlit diagnostics derived from the displaced " +
            "terrain heightfield. Curvature uses the exposed Scale value " +
            "as a world-space sampling radius and updates interactively.\n\n" +

            "Height, Slope, Curvature, and Contours require a ready Height " +
            "Preview. Chunk Grid, Height Tile Grid, World Boundary, and " +
            "LOD Regions are spatial diagnostics and remain usable without " +
            "height preview data.\n\n" +

            "Visualization is applied transiently with renderer " +
            "MaterialPropertyBlocks and is disabled before Play Mode.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }

    private static void DrawCurvatureScalePresetButton(
        float scale
    )
    {
        if (
            GUILayout.Button(
                scale.ToString("0"),
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainAuthoringVisualizationController
                .CurvatureScale =
                    scale;
        }
    }

    private static void DrawContourPresetButton(
        float interval
    )
    {
        if (
            GUILayout.Button(
                interval.ToString("0"),
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainAuthoringVisualizationController
                .ContourInterval =
                    interval;
        }
    }

    private static void DrawVisualizationToggle(
        string label,
        bool currentValue,
        System.Action<bool> setter
    )
    {
        EditorGUI.BeginChangeCheck();

        bool newValue =
            EditorGUILayout.Toggle(
                label,
                currentValue
            );

        if (
            EditorGUI.EndChangeCheck()
            &&
            setter != null
        )
        {
            setter(
                newValue
            );
        }
    }
}
