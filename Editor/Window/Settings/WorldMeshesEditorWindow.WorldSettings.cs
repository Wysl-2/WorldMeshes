using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    // =====================================================
    // WORLD SETTINGS
    // =====================================================

    private const string WorldSettingsFolder =
        WorldMeshesPaths.Configuration;

    private const string DefaultWorldSettingsPath =
        WorldMeshesPaths.WorldSettingsAssetPath;

    [SerializeField]
    private WorldSettings worldSettings;

    // =====================================================
    // WORLD / CHUNK INPUTS
    // =====================================================

    [SerializeField]
    private int inputGridWidth = 10;

    [SerializeField]
    private int inputGridHeight = 10;

    [SerializeField]
    private float inputChunkSize = 128f;

    [SerializeField]
    private int inputLOD0Resolution = 128;

    private void DrawWorldSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "World Settings",
            EditorStyles.boldLabel
        );

        // -------------------------------------------------
        // WorldSettings asset
        // -------------------------------------------------

        EditorGUI.BeginChangeCheck();

        WorldSettings selectedSettings =
            (WorldSettings)
            EditorGUILayout.ObjectField(
                "Settings Asset",
                worldSettings,
                typeof(WorldSettings),
                false
            );

        if (EditorGUI.EndChangeCheck())
        {
            worldSettings =
                selectedSettings;

            if (worldSettings != null)
            {
                LoadWorldSettingsIntoEditor();
            }

            Repaint();
        }

        // -------------------------------------------------
        // No WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "No WorldSettings asset is assigned. " +
                "Create one before configuring the world.",
                MessageType.Warning
            );

            if (
                GUILayout.Button(
                    "Create World Settings",
                    GUILayout.ExpandWidth(true)
                )
            )
            {
                CreateWorldSettingsAsset();
            }

            GUILayout.EndVertical();

            return;
        }

        GUILayout.Space(5f);

        // -------------------------------------------------
        // Editable settings
        // -------------------------------------------------

        float oldLabelWidth =
            EditorGUIUtility.labelWidth;

        EditorGUIUtility.labelWidth =
            120f;

        inputGridWidth =
            EditorGUILayout.IntField(
                "Grid Width",
                inputGridWidth
            );

        inputGridHeight =
            EditorGUILayout.IntField(
                "Grid Height",
                inputGridHeight
            );

        inputChunkSize =
            EditorGUILayout.FloatField(
                "Chunk Size",
                inputChunkSize
            );

        inputLOD0Resolution =
            EditorGUILayout.IntField(
                "LOD0 Resolution",
                inputLOD0Resolution
            );

        EditorGUIUtility.labelWidth =
            oldLabelWidth;

        // -------------------------------------------------
        // Validate temporary input
        // -------------------------------------------------

        inputGridWidth =
            Mathf.Max(
                1,
                inputGridWidth
            );

        inputGridHeight =
            Mathf.Max(
                1,
                inputGridHeight
            );

        inputChunkSize =
            Mathf.Max(
                0.01f,
                inputChunkSize
            );

        inputLOD0Resolution =
            Mathf.Max(
                1,
                inputLOD0Resolution
            );

        GUILayout.Space(5f);

        // -------------------------------------------------
        // Saved settings
        // -------------------------------------------------

        EditorGUILayout.LabelField(
            "Saved Grid",
            $"{worldSettings.gridWidth} x " +
            $"{worldSettings.gridHeight}"
        );

        EditorGUILayout.LabelField(
            "Saved Chunk Size",
            worldSettings.chunkSize.ToString()
        );

        EditorGUILayout.LabelField(
            "Saved LOD0 Resolution",
            worldSettings.lod0Resolution.ToString()
        );

        GUILayout.Space(5f);

        // -------------------------------------------------
        // Update / reload
        // -------------------------------------------------

        if (
            GUILayout.Button(
                "Update World Settings",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            UpdateWorldSettings();
        }

        if (
            GUILayout.Button(
                "Reload From Asset",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            LoadWorldSettingsIntoEditor();
        }

        // -------------------------------------------------
        // Reset generated data
        // -------------------------------------------------

        GUILayout.Space(10f);

        EditorGUILayout.HelpBox(
            "Reset Generated World deletes all generated " +
            "terrain data:\n\n" +

            "• LOD0 base mesh\n" +
            "• All generated chunk mesh assets\n" +
            "• All generated heightmap tiles\n" +
            "• Heightmap generation manifest\n" +
            "• WorldRoot scene hierarchy\n\n" +

            "The WorldSettings asset and all current world, " +
            "chunk, and height-generation settings are preserved.",
            MessageType.Warning
        );

        if (
            GUILayout.Button(
                "Reset Generated World",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainWorldResetUtility
                .ResetGeneratedWorld(
                    worldSettings
                );
        }

        GUILayout.EndVertical();
    }

    private void CreateWorldSettingsAsset()
    {
        EnsureWorldSettingsFolderExists();

        WorldSettings existingSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                DefaultWorldSettingsPath
            );

        if (existingSettings != null)
        {
            worldSettings =
                existingSettings;

            LoadWorldSettingsIntoEditor();

            Selection.activeObject =
                worldSettings;

            Debug.Log(
                $"Loaded existing WorldSettings:\n" +
                $"{DefaultWorldSettingsPath}"
            );

            return;
        }

        WorldSettings newSettings =
            CreateInstance<WorldSettings>();

        // -------------------------------------------------
        // World / chunks
        // -------------------------------------------------

        newSettings.gridWidth =
            Mathf.Max(
                1,
                inputGridWidth
            );

        newSettings.gridHeight =
            Mathf.Max(
                1,
                inputGridHeight
            );

        newSettings.chunkSize =
            Mathf.Max(
                0.01f,
                inputChunkSize
            );

        newSettings.lod0Resolution =
            Mathf.Max(
                1,
                inputLOD0Resolution
            );

        // -------------------------------------------------
        // Height generation
        // -------------------------------------------------

        newSettings.heightTileChunkSpan =
            Mathf.Max(
                1,
                inputHeightTileChunkSpan
            );

        newSettings.heightSeed =
            inputHeightSeed;

        newSettings.heightNoiseScale =
            Mathf.Max(
                0.0001f,
                inputHeightNoiseScale
            );

        newSettings.heightBaseHeight =
            inputHeightBaseHeight;

        newSettings.heightAmplitude =
            Mathf.Max(
                0f,
                inputHeightAmplitude
            );

        newSettings.heightOctaves =
            Mathf.Clamp(
                inputHeightOctaves,
                1,
                12
            );

        newSettings.heightPersistence =
            Mathf.Clamp01(
                inputHeightPersistence
            );

        newSettings.heightLacunarity =
            Mathf.Max(
                1f,
                inputHeightLacunarity
            );

        // -------------------------------------------------
        // Create asset
        // -------------------------------------------------

        AssetDatabase.CreateAsset(
            newSettings,
            DefaultWorldSettingsPath
        );

        AssetDatabase.SaveAssets();

        worldSettings =
            newSettings;

        LoadWorldSettingsIntoEditor();

        Selection.activeObject =
            worldSettings;

        Debug.Log(
            $"Created WorldSettings:\n" +
            $"{DefaultWorldSettingsPath}"
        );

        Repaint();
    }

    private void EnsureWorldSettingsFolderExists()
    {
        if (
            !AssetDatabase.IsValidFolder(
                WorldSettingsFolder
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Root,
                "Configuration"
            );
        }
    }

    private void LoadWorldSettingsIntoEditor()
    {
        if (worldSettings == null)
        {
            return;
        }

        // -------------------------------------------------
        // World / chunks
        // -------------------------------------------------

        inputGridWidth =
            Mathf.Max(
                1,
                worldSettings.gridWidth
            );

        inputGridHeight =
            Mathf.Max(
                1,
                worldSettings.gridHeight
            );

        inputChunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        inputLOD0Resolution =
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            );

        // -------------------------------------------------
        // Collision generation
        // -------------------------------------------------

        inputCollisionResolution =
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            );

        // -------------------------------------------------
        // Height generation
        // -------------------------------------------------

        inputHeightTileChunkSpan =
            Mathf.Max(
                1,
                worldSettings.heightTileChunkSpan
            );

        inputHeightSeed =
            worldSettings.heightSeed;

        inputHeightNoiseScale =
            Mathf.Max(
                0.0001f,
                worldSettings.heightNoiseScale
            );

        inputHeightBaseHeight =
            worldSettings.heightBaseHeight;

        inputHeightAmplitude =
            Mathf.Max(
                0f,
                worldSettings.heightAmplitude
            );

        inputHeightOctaves =
            Mathf.Clamp(
                worldSettings.heightOctaves,
                1,
                12
            );

        inputHeightPersistence =
            Mathf.Clamp01(
                worldSettings.heightPersistence
            );

        inputHeightLacunarity =
            Mathf.Max(
                1f,
                worldSettings.heightLacunarity
            );



        Repaint();
    }

    private void UpdateWorldSettings()
    {
        if (worldSettings == null)
        {
            return;
        }

        Undo.RecordObject(
            worldSettings,
            "Update World Settings"
        );

        worldSettings.gridWidth =
            Mathf.Max(
                1,
                inputGridWidth
            );

        worldSettings.gridHeight =
            Mathf.Max(
                1,
                inputGridHeight
            );

        worldSettings.chunkSize =
            Mathf.Max(
                0.01f,
                inputChunkSize
            );

        worldSettings.lod0Resolution =
            Mathf.Max(
                1,
                inputLOD0Resolution
            );

        /*
         * Do NOT change:
         *
         * lastSyncedChunkSize
         * lastSyncedLOD0Resolution
         *
         * Those describe the currently generated chunk
         * assets, not the requested world settings.
         */

        EditorUtility.SetDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );

        Repaint();

        Debug.Log(
            "World settings updated.\n\n" +
            $"Grid: " +
            $"{worldSettings.gridWidth} x " +
            $"{worldSettings.gridHeight}\n" +
            $"Chunk Size: " +
            $"{worldSettings.chunkSize}\n" +
            $"LOD0 Resolution: " +
            $"{worldSettings.lod0Resolution}"
        );
    }
}
