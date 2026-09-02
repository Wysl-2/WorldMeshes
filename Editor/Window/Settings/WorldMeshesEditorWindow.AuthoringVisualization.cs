using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    // =====================================================
    // SCREE MATERIAL PROPERTY IDS
    // =====================================================

    private static readonly int ScreeMapPropertyId =
        Shader.PropertyToID(
            "_ScreeMap"
        );

    private static readonly int ScreeColorPropertyId =
        Shader.PropertyToID(
            "_ScreeColor"
        );

    private static readonly int ScreeMapWorldSizePropertyId =
        Shader.PropertyToID(
            "_ScreeMapWorldSize"
        );

    private static readonly int ScreeTriplanarSharpnessPropertyId =
        Shader.PropertyToID(
            "_ScreeTriplanarSharpness"
        );

    private static readonly int ScreeSlopeMinPropertyId =
        Shader.PropertyToID(
            "_ScreeSlopeMin"
        );

    private static readonly int ScreeSlopePreferredMinPropertyId =
        Shader.PropertyToID(
            "_ScreeSlopePreferredMin"
        );

    private static readonly int ScreeSlopePreferredMaxPropertyId =
        Shader.PropertyToID(
            "_ScreeSlopePreferredMax"
        );

    private static readonly int ScreeSlopeMaxPropertyId =
        Shader.PropertyToID(
            "_ScreeSlopeMax"
        );

    private static readonly int ScreeCurvatureScalePropertyId =
        Shader.PropertyToID(
            "_ScreeCurvatureScale"
        );

    private static readonly int ScreeConvexRejectStartPropertyId =
        Shader.PropertyToID(
            "_ScreeConvexRejectStart"
        );

    private static readonly int ScreeConvexRejectEndPropertyId =
        Shader.PropertyToID(
            "_ScreeConvexRejectEnd"
        );

    private static readonly int ScreeGeologyScalePropertyId =
        Shader.PropertyToID(
            "_ScreeGeologyScale"
        );

    private static readonly int ScreeGeologyStrengthPropertyId =
        Shader.PropertyToID(
            "_ScreeGeologyStrength"
        );

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

        // =================================================
        // SCREE SUITABILITY
        // =================================================

        if (
            baseMode ==
                TerrainAuthoringVisualizationMode.ScreeSuitability
        )
        {
            DrawScreeSuitabilitySettings();
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
            "Lit uses the normal terrain PBR path. Height, Slope, Curvature, " +
            "and Scree Suitability are unlit diagnostics derived from the " +
            "displaced terrain heightfield. Curvature uses the exposed Scale " +
            "value as a world-space sampling radius and updates " +
            "interactively. Scree Suitability displays the exact 0..1 mask " +
            "used to blend the scree surface in Lit mode.\n\n" +

            "Height, Slope, Curvature, Scree Suitability, and Contours " +
            "require a ready Height Preview. Chunk Grid, Height Tile Grid, " +
            "World Boundary, and LOD Regions are spatial diagnostics and " +
            "remain usable without height preview data.\n\n" +

            "Visualization is applied transiently with renderer " +
            "MaterialPropertyBlocks and is disabled before Play Mode.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }

    private static void DrawScreeSuitabilitySettings()
    {
        GUILayout.Space(
            5f
        );

        GUILayout.Label(
            "Scree Suitability",
            EditorStyles.boldLabel
        );

        Material terrainMaterial =
            AssetDatabase.LoadAssetAtPath<Material>(
                WorldMeshesPaths
                    .ClipmapTerrainMaterialPath
            );

        if (terrainMaterial == null)
        {
            EditorGUILayout.HelpBox(
                "The clipmap terrain material could not be loaded from:\n" +
                WorldMeshesPaths.ClipmapTerrainMaterialPath,
                MessageType.Error
            );

            return;
        }

        if (
            !terrainMaterial.HasProperty(
                ScreeMapPropertyId
            )
        )
        {
            EditorGUILayout.HelpBox(
                "The current clipmap terrain shader does not expose the " +
                "scree surface properties. Install the shader portion of " +
                "the Scree Suitability package first.",
                MessageType.Error
            );

            return;
        }

        Texture screeMap =
            terrainMaterial.GetTexture(
                ScreeMapPropertyId
            );

        Color screeColor =
            terrainMaterial.GetColor(
                ScreeColorPropertyId
            );

        float screeMapWorldSize =
            terrainMaterial.GetFloat(
                ScreeMapWorldSizePropertyId
            );

        float screeTriplanarSharpness =
            terrainMaterial.GetFloat(
                ScreeTriplanarSharpnessPropertyId
            );

        float slopeMin =
            terrainMaterial.GetFloat(
                ScreeSlopeMinPropertyId
            );

        float slopePreferredMin =
            terrainMaterial.GetFloat(
                ScreeSlopePreferredMinPropertyId
            );

        float slopePreferredMax =
            terrainMaterial.GetFloat(
                ScreeSlopePreferredMaxPropertyId
            );

        float slopeMax =
            terrainMaterial.GetFloat(
                ScreeSlopeMaxPropertyId
            );

        float curvatureScale =
            terrainMaterial.GetFloat(
                ScreeCurvatureScalePropertyId
            );

        float convexRejectStart =
            terrainMaterial.GetFloat(
                ScreeConvexRejectStartPropertyId
            );

        float convexRejectEnd =
            terrainMaterial.GetFloat(
                ScreeConvexRejectEndPropertyId
            );

        float geologyScale =
            terrainMaterial.GetFloat(
                ScreeGeologyScalePropertyId
            );

        float geologyStrength =
            terrainMaterial.GetFloat(
                ScreeGeologyStrengthPropertyId
            );

        EditorGUI.BeginChangeCheck();

        Texture newScreeMap =
            (Texture)EditorGUILayout.ObjectField(
                "Scree Texture",
                screeMap,
                typeof(Texture2D),
                false
            );

        Color newScreeColor =
            EditorGUILayout.ColorField(
                "Scree Tint",
                screeColor
            );

        float newScreeMapWorldSize =
            EditorGUILayout.Slider(
                "Texture World Size (m)",
                screeMapWorldSize,
                0.25f,
                128f
            );

        float newScreeTriplanarSharpness =
            EditorGUILayout.Slider(
                "Triplanar Sharpness",
                screeTriplanarSharpness,
                1f,
                16f
            );

        GUILayout.Space(
            4f
        );

        GUILayout.Label(
            "Slope",
            EditorStyles.miniBoldLabel
        );

        float newSlopeMin =
            EditorGUILayout.Slider(
                "Minimum (deg)",
                slopeMin,
                0f,
                90f
            );

        float newSlopePreferredMin =
            EditorGUILayout.Slider(
                "Preferred Min (deg)",
                slopePreferredMin,
                0f,
                90f
            );

        float newSlopePreferredMax =
            EditorGUILayout.Slider(
                "Preferred Max (deg)",
                slopePreferredMax,
                0f,
                90f
            );

        float newSlopeMax =
            EditorGUILayout.Slider(
                "Maximum (deg)",
                slopeMax,
                0f,
                90f
            );

        GUILayout.Space(
            4f
        );

        GUILayout.Label(
            "Curvature",
            EditorStyles.miniBoldLabel
        );

        float newCurvatureScale =
            EditorGUILayout.Slider(
                "Scale (m)",
                curvatureScale,
                1f,
                256f
            );

        float newConvexRejectStart =
            EditorGUILayout.Slider(
                "Convex Reject Start",
                convexRejectStart,
                0f,
                0.5f
            );

        float newConvexRejectEnd =
            EditorGUILayout.Slider(
                "Convex Reject End",
                convexRejectEnd,
                0f,
                0.5f
            );

        GUILayout.Space(
            4f
        );

        GUILayout.Label(
            "Geological Variation",
            EditorStyles.miniBoldLabel
        );

        float newGeologyScale =
            EditorGUILayout.Slider(
                "Patch Scale (m)",
                geologyScale,
                1f,
                512f
            );

        float newGeologyStrength =
            EditorGUILayout.Slider(
                "Patch Strength",
                geologyStrength,
                0f,
                1f
            );

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(
                terrainMaterial,
                "Change Scree Suitability"
            );

            terrainMaterial.SetTexture(
                ScreeMapPropertyId,
                newScreeMap
            );

            terrainMaterial.SetColor(
                ScreeColorPropertyId,
                newScreeColor
            );

            terrainMaterial.SetFloat(
                ScreeMapWorldSizePropertyId,
                Mathf.Max(
                    0.0001f,
                    newScreeMapWorldSize
                )
            );

            terrainMaterial.SetFloat(
                ScreeTriplanarSharpnessPropertyId,
                Mathf.Clamp(
                    newScreeTriplanarSharpness,
                    1f,
                    16f
                )
            );

            terrainMaterial.SetFloat(
                ScreeSlopeMinPropertyId,
                Mathf.Clamp(
                    newSlopeMin,
                    0f,
                    90f
                )
            );

            terrainMaterial.SetFloat(
                ScreeSlopePreferredMinPropertyId,
                Mathf.Clamp(
                    newSlopePreferredMin,
                    0f,
                    90f
                )
            );

            terrainMaterial.SetFloat(
                ScreeSlopePreferredMaxPropertyId,
                Mathf.Clamp(
                    newSlopePreferredMax,
                    0f,
                    90f
                )
            );

            terrainMaterial.SetFloat(
                ScreeSlopeMaxPropertyId,
                Mathf.Clamp(
                    newSlopeMax,
                    0f,
                    90f
                )
            );

            terrainMaterial.SetFloat(
                ScreeCurvatureScalePropertyId,
                Mathf.Max(
                    0.25f,
                    newCurvatureScale
                )
            );

            terrainMaterial.SetFloat(
                ScreeConvexRejectStartPropertyId,
                Mathf.Max(
                    0f,
                    newConvexRejectStart
                )
            );

            terrainMaterial.SetFloat(
                ScreeConvexRejectEndPropertyId,
                Mathf.Max(
                    0f,
                    newConvexRejectEnd
                )
            );

            terrainMaterial.SetFloat(
                ScreeGeologyScalePropertyId,
                Mathf.Max(
                    0.001f,
                    newGeologyScale
                )
            );

            terrainMaterial.SetFloat(
                ScreeGeologyStrengthPropertyId,
                Mathf.Clamp01(
                    newGeologyStrength
                )
            );

            EditorUtility.SetDirty(
                terrainMaterial
            );

            TerrainAuthoringVisualizationController
                .RequestReapply();
        }

        EditorGUILayout.HelpBox(
            "The suitability mask is shared by this diagnostic and Lit " +
            "terrain rendering. It combines a preferred slope band, " +
            "rejection of strongly convex terrain, and broad geological " +
            "variation. Black means unsuitable and red means strongest " +
            "scree coverage. In Lit mode the existing steep-rock blend " +
            "retains priority over scree.",
            MessageType.None
        );
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
