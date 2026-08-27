using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    // =====================================================
    // CLIPMAP INPUTS
    // =====================================================

    [SerializeField]
    private int inputClipmapCenterResolution = 64;

    [SerializeField]
    private int inputClipmapLevelCount = 6;

    [SerializeField]
    private int inputClipmapBaseSampleStep = 1;

    private WorldSettings clipmapInputSource;

    private void LoadClipmapSettingsIntoEditor()
    {
        if (worldSettings == null)
        {
            clipmapInputSource =
                null;

            return;
        }

        inputClipmapCenterResolution =
            Mathf.Max(
                8,
                worldSettings.clipmapCenterResolution
            );

        inputClipmapLevelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                1,
                10
            );

        inputClipmapBaseSampleStep =
            Mathf.Max(
                1,
                worldSettings.clipmapBaseSampleStep
            );

        clipmapInputSource =
            worldSettings;

        Repaint();
    }

    private void UpdateClipmapSettings()
    {
        if (worldSettings == null)
        {
            return;
        }

        int centerResolution =
            Mathf.Max(
                8,
                inputClipmapCenterResolution
            );

        int levelCount =
            Mathf.Clamp(
                inputClipmapLevelCount,
                1,
                10
            );

        int baseSampleStep =
            Mathf.Max(
                1,
                inputClipmapBaseSampleStep
            );

        // -------------------------------------------------
        // Validate resolution
        // -------------------------------------------------

        if (
            centerResolution % 4 != 0
        )
        {
            EditorUtility.DisplayDialog(
                "Invalid Clipmap Resolution",

                "Clipmap Center Resolution must be evenly " +
                "divisible by 4.\n\n" +

                $"Current Resolution: " +
                $"{centerResolution}\n\n" +

                "Examples:\n" +
                "32, 64, 128, 256",

                "OK"
            );

            return;
        }

        // -------------------------------------------------
        // Record undo
        // -------------------------------------------------

        Undo.RecordObject(
            worldSettings,
            "Update Clipmap Settings"
        );

        // -------------------------------------------------
        // Save settings
        // -------------------------------------------------

        worldSettings.clipmapCenterResolution =
            centerResolution;

        worldSettings.clipmapLevelCount =
            levelCount;

        worldSettings.clipmapBaseSampleStep =
            baseSampleStep;

        EditorUtility.SetDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );

        clipmapInputSource =
            worldSettings;

        // -------------------------------------------------
        // Derived values
        // -------------------------------------------------

        float baseSpacing =
            worldSettings.ClipmapBaseSpacing;

        float outerDiameter =
            centerResolution *
            baseSpacing *
            Mathf.Pow(
                2f,
                levelCount - 1
            );

        int generatedAssetCount =
            1 +
            Mathf.Max(
                0,
                levelCount - 1
            )
            *
            2;

        Repaint();

        Debug.Log(
            "Clipmap settings updated.\n\n" +

            $"Center Resolution: " +
            $"{centerResolution}\n" +

            $"LOD Levels: " +
            $"{levelCount}\n" +

            $"Base Sample Step: " +
            $"{baseSampleStep}\n" +

            $"Base Vertex Spacing: " +
            $"{baseSpacing}\n\n" +

            $"Expected Clipmap Assets: " +
            $"{generatedAssetCount}\n" +

            $"Outer Coverage: " +
            $"{outerDiameter} x " +
            $"{outerDiameter}"
        );
    }

    private void DrawClipmapGenerationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Clipmap Generation",
            EditorStyles.boldLabel
        );

        // -------------------------------------------------
        // No settings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            clipmapInputSource =
                null;

            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "generating clipmap geometry.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        // -------------------------------------------------
        // Load inputs when WorldSettings changes
        // -------------------------------------------------

        if (
            clipmapInputSource !=
            worldSettings
        )
        {
            LoadClipmapSettingsIntoEditor();
        }

        // -------------------------------------------------
        // Inputs
        // -------------------------------------------------

        float oldLabelWidth =
            EditorGUIUtility.labelWidth;

        EditorGUIUtility.labelWidth =
            150f;

        inputClipmapCenterResolution =
            EditorGUILayout.IntField(
                "Center Resolution",
                inputClipmapCenterResolution
            );

        inputClipmapLevelCount =
            EditorGUILayout.IntField(
                "LOD Levels",
                inputClipmapLevelCount
            );

        inputClipmapBaseSampleStep =
            EditorGUILayout.IntField(
                "Base Sample Step",
                inputClipmapBaseSampleStep
            );

        EditorGUIUtility.labelWidth =
            oldLabelWidth;

        // -------------------------------------------------
        // Clamp basic ranges
        // -------------------------------------------------

        inputClipmapCenterResolution =
            Mathf.Max(
                8,
                inputClipmapCenterResolution
            );

        inputClipmapLevelCount =
            Mathf.Clamp(
                inputClipmapLevelCount,
                1,
                10
            );

        inputClipmapBaseSampleStep =
            Mathf.Max(
                1,
                inputClipmapBaseSampleStep
            );

        // -------------------------------------------------
        // Validation
        // -------------------------------------------------

        bool resolutionValid =
            inputClipmapCenterResolution %
            4
            ==
            0;

        if (!resolutionValid)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                "Center Resolution must be evenly divisible " +
                "by 4 so adjacent 2:1 LOD boundaries can be " +
                "stitched exactly.",
                MessageType.Warning
            );
        }

        // -------------------------------------------------
        // Derived values
        // -------------------------------------------------

        float heightSampleSpacing =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            )
            /
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            );

        float stagedBaseSpacing =
            heightSampleSpacing *
            inputClipmapBaseSampleStep;

        float stagedOuterDiameter =
            inputClipmapCenterResolution *
            stagedBaseSpacing *
            Mathf.Pow(
                2f,
                inputClipmapLevelCount - 1
            );

        int generatedAssetCount =
            1
            +
            Mathf.Max(
                0,
                inputClipmapLevelCount - 1
            )
            *
            2;

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Height Sample Spacing",
            heightSampleSpacing.ToString()
        );

        EditorGUILayout.LabelField(
            "Base Vertex Spacing",
            stagedBaseSpacing.ToString()
        );

        EditorGUILayout.LabelField(
            "Generated Assets",
            generatedAssetCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Outer Coverage",
            $"{stagedOuterDiameter} x " +
            $"{stagedOuterDiameter}"
        );

        // -------------------------------------------------
        // LOD spacing summary
        // -------------------------------------------------

        GUILayout.Space(5f);

        string spacingSummary =
            "";

        for (
            int level = 0;
            level < inputClipmapLevelCount;
            level++
        )
        {
            float levelSpacing =
                stagedBaseSpacing *
                Mathf.Pow(
                    2f,
                    level
                );

            if (level > 0)
            {
                spacingSummary +=
                    "\n";
            }

            spacingSummary +=
                $"LOD{level}: " +
                $"{levelSpacing}";
        }

        EditorGUILayout.HelpBox(
            "Vertex Spacing\n\n" +
            spacingSummary,
            MessageType.Info
        );

        // -------------------------------------------------
        // Explanation
        // -------------------------------------------------

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Generates flat, pre-built geometry for the " +
            "runtime terrain clipmap.\n\n" +

            "LOD0 is a complete square. Each coarser level " +
            "is a hollow ring, with a dedicated 2:1 stitch " +
            "mesh joining it to the previous level.\n\n" +

            "No height displacement is performed at this stage.",
            MessageType.Info
        );

        // -------------------------------------------------
        // Update settings
        // -------------------------------------------------

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Update Clipmap Settings",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            UpdateClipmapSettings();
        }

        if (
            GUILayout.Button(
                "Reload Clipmap Settings",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            LoadClipmapSettingsIntoEditor();
        }

        // -------------------------------------------------
        // Saved-settings warning
        // -------------------------------------------------

        GUILayout.Space(10f);

        EditorGUILayout.HelpBox(
            "Clipmap generation uses the values currently " +
            "saved in WorldSettings.\n\n" +

            "Press Update Clipmap Settings before generating " +
            "if you have changed the fields above.",
            MessageType.Warning
        );

        // -------------------------------------------------
        // Generate
        // -------------------------------------------------

        if (
            GUILayout.Button(
                "Generate / Regenerate Clipmap Meshes",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainClipmapMeshGenerator
                .GenerateClipmapMeshes(
                    worldSettings
                );
        }

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Output Folder",
            TerrainClipmapMeshGenerator
                .ClipmapMeshFolder
        );

        // =====================================================
        // GEOMETRY VALIDATION
        // =====================================================

        GUILayout.Space(10f);

        EditorGUILayout.HelpBox(
            "Validates the complete generated clipmap topology.\n\n" +

            "The center, rings, and stitch meshes are treated " +
            "as one combined surface and checked for cracks, " +
            "holes, overlapping triangles, non-manifold edges, " +
            "degenerate triangles, incorrect winding, and " +
            "incorrect overall coverage.\n\n" +

            "No generated assets are modified.",
            MessageType.Info
        );

        if (
            GUILayout.Button(
                "Validate Clipmap Geometry",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainClipmapGeometryValidator
                .ValidateClipmapGeometry(
                    worldSettings
                );
        }

        GUILayout.EndVertical();
    }
}
