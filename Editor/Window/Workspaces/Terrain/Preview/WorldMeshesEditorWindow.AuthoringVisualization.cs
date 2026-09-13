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
        // RAW TERRAIN ANALYSIS
        // =================================================

        if (
            TerrainAuthoringVisualizationController
                .IsRawAnalysisVisualizationMode(
                    baseMode
                )
        )
        {
            GUILayout.Space(
                5f
            );

            if (
                TerrainAuthoringVisualizationController
                    .TryGetAnalysisVisualizationKey(
                        baseMode,
                        out TerrainAnalysisKey analysisKey
                    )
                &&
                TerrainAnalysisRegistry
                    .TryGetDefinition(
                        analysisKey.Type,
                        out TerrainAnalysisDefinition analysisDefinition
                    )
                &&
                analysisDefinition != null
            )
            {
                GUILayout.Label(
                    analysisDefinition.DisplayName,
                    EditorStyles.boldLabel
                );

                if (analysisDefinition.RequiresScale)
                {
                    float analysisScale =
                        TerrainAuthoringVisualizationController
                            .GetAnalysisVisualizationScale(
                                baseMode
                            );

                    EditorGUI.BeginChangeCheck();

                    float newAnalysisScale =
                        EditorGUILayout.Slider(
                            "Scale (m)",
                            analysisScale,
                            1f,
                            256f
                        );

                    if (EditorGUI.EndChangeCheck())
                    {
                        TerrainAuthoringVisualizationController
                            .SetAnalysisVisualizationScale(
                                baseMode,
                                newAnalysisScale
                            );
                    }

                    GUILayout.BeginHorizontal();

                    GUILayout.Label(
                        "Presets",
                        GUILayout.Width(
                            100f
                        )
                    );

                    DrawAnalysisScalePresetButton(
                        baseMode,
                        2f
                    );

                    DrawAnalysisScalePresetButton(
                        baseMode,
                        8f
                    );

                    DrawAnalysisScalePresetButton(
                        baseMode,
                        16f
                    );

                    DrawAnalysisScalePresetButton(
                        baseMode,
                        32f
                    );

                    DrawAnalysisScalePresetButton(
                        baseMode,
                        64f
                    );

                    GUILayout.EndHorizontal();
                }

                string analysisHelp;

                switch (analysisKey.Type)
                {
                    case TerrainAnalysisType.Slope:
                        analysisHelp =
                            "Slope displays the cached Terrain Analysis slope field in degrees. " +
                            "This is now the same raw analysis data consumed by generation/suitability systems, " +
                            "rather than a separate fragment-normal approximation.";

                        break;

                    case TerrainAnalysisType.Curvature:
                        analysisHelp =
                            "Curvature is signed: blue is concave, green is approximately planar, " +
                            "and red is convex. Scale is the world-space sampling radius.";

                        break;

                    case TerrainAnalysisType.Roughness:
                        analysisHelp =
                            "Roughness is a dimensionless RMS deviation from a local planar trend. " +
                            "Smooth planar slopes approach zero; broken or irregular terrain becomes stronger.";

                        break;

                    case TerrainAnalysisType.LocalRelief:
                        analysisHelp =
                            "Local Relief is the local elevation range in metres (maximum sampled height minus minimum sampled height) " +
                            "within the selected world-space radius.";

                        break;

                    default:
                        analysisHelp =
                            "This diagnostic samples the registered Terrain Analysis layer directly.";

                        break;
                }

                EditorGUILayout.HelpBox(
                    analysisHelp,
                    MessageType.None
                );
            }
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
            "Lit uses the normal terrain PBR path. Height and Scree Suitability are " +
            "special diagnostics; Slope, Curvature, Roughness, and Local Relief " +
            "all use the shared cached Terrain Analysis visualization path. " +
            "Scale-dependent analyses update interactively without accumulating " +
            "multiple scratch Texture2DArray layers. Scree Suitability displays " +
            "the exact 0..1 mask used to blend the scree surface in Lit mode.\n\n" +

            "Height, Slope, Curvature, Roughness, Local Relief, Scree Suitability, and Contours " +
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
                "Scree surface properties.",
                MessageType.Error
            );

            return;
        }

        TerrainSurfaceSettings surfaceSettings =
            TerrainSurfaceSettingsEditorUtility
                .LoadOrCreate(
                    out string surfaceSettingsError
                );

        if (surfaceSettings == null)
        {
            EditorGUILayout.HelpBox(
                "TerrainSurfaceSettings could not be loaded or created.\n\n" +
                surfaceSettingsError,
                MessageType.Error
            );

            return;
        }

        ScreeSettings scree =
            surfaceSettings.Scree;

        // =================================================
        // SURFACE APPEARANCE - MATERIAL OWNED
        // =================================================

        GUILayout.Space(
            4f
        );

        GUILayout.Label(
            "Surface Appearance",
            EditorStyles.miniBoldLabel
        );

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

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(
                terrainMaterial,
                "Change Scree Surface Appearance"
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

            EditorUtility.SetDirty(
                terrainMaterial
            );

            TerrainAuthoringVisualizationController
                .RequestReapply();
        }

        // =================================================
        // SUITABILITY - TERRAIN SURFACE SETTINGS OWNED
        // =================================================

        GUILayout.Space(
            6f
        );

        GUILayout.Label(
            "Suitability Rules",
            EditorStyles.miniBoldLabel
        );

        EditorGUILayout.HelpBox(
            "Suitability rules are stored in TerrainSurfaceSettings.asset. " +
            "The terrain material now owns Scree appearance only.",
            MessageType.None
        );

        EditorGUI.BeginChangeCheck();

        GUILayout.Space(
            2f
        );

        GUILayout.Label(
            "Slope",
            EditorStyles.miniBoldLabel
        );

        float newSlopeMin =
            EditorGUILayout.Slider(
                "Minimum (deg)",
                scree.slopeMin,
                0f,
                90f
            );

        float newSlopePreferredMin =
            EditorGUILayout.Slider(
                "Preferred Min (deg)",
                scree.slopePreferredMin,
                0f,
                90f
            );

        float newSlopePreferredMax =
            EditorGUILayout.Slider(
                "Preferred Max (deg)",
                scree.slopePreferredMax,
                0f,
                90f
            );

        float newSlopeMax =
            EditorGUILayout.Slider(
                "Maximum (deg)",
                scree.slopeMax,
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
                scree.curvatureScale,
                1f,
                256f
            );

        float newConvexRejectStart =
            EditorGUILayout.Slider(
                "Convex Reject Start",
                scree.convexRejectStart,
                0f,
                0.5f
            );

        float newConvexRejectEnd =
            EditorGUILayout.Slider(
                "Convex Reject End",
                scree.convexRejectEnd,
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
                scree.geologyScale,
                1f,
                512f
            );

        float newGeologyStrength =
            EditorGUILayout.Slider(
                "Patch Strength",
                scree.geologyStrength,
                0f,
                1f
            );

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(
                surfaceSettings,
                "Change Scree Suitability"
            );

            scree.slopeMin =
                newSlopeMin;

            scree.slopePreferredMin =
                newSlopePreferredMin;

            scree.slopePreferredMax =
                newSlopePreferredMax;

            scree.slopeMax =
                newSlopeMax;

            scree.curvatureScale =
                newCurvatureScale;

            scree.convexRejectStart =
                newConvexRejectStart;

            scree.convexRejectEnd =
                newConvexRejectEnd;

            scree.geologyScale =
                newGeologyScale;

            scree.geologyStrength =
                newGeologyStrength;

            scree.Sanitize();

            EditorUtility.SetDirty(
                surfaceSettings
            );

            TerrainRuntimeInvalidationService
                .InvalidateSurfaceSettingsChanged();

            TerrainAuthoringVisualizationController
                .RequestReapply();
        }

        EditorGUILayout.HelpBox(
            "The suitability mask combines a preferred slope band, " +
            "rejection of strongly convex terrain, and broad geological " +
            "variation. Black means unsuitable and red means strongest " +
            "Scree coverage. In Lit mode the existing steep-rock blend " +
            "retains priority over Scree.",
            MessageType.None
        );
    }

    private static void DrawAnalysisScalePresetButton(
        TerrainAuthoringVisualizationMode mode,
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
                .SetAnalysisVisualizationScale(
                    mode,
                    scale
                );
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
