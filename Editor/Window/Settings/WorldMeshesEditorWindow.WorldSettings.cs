using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
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
    // WORLD / HEIGHTFIELD INPUTS
    // =====================================================

    [SerializeField]
    private int inputGridWidth =
        10;

    [SerializeField]
    private int inputGridHeight =
        10;

    [SerializeField]
    private float inputChunkSize =
        128f;

    /*
     * Native heightfield intervals per terrain chunk.
     */
    [SerializeField]
    private int inputHeightfieldResolutionPerChunk =
        128;

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

        float oldLabelWidth =
            EditorGUIUtility.labelWidth;

        EditorGUIUtility.labelWidth =
            185f;

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

        inputHeightfieldResolutionPerChunk =
            EditorGUILayout.IntField(
                "Heightfield Resolution / Chunk",
                inputHeightfieldResolutionPerChunk
            );

        EditorGUIUtility.labelWidth =
            oldLabelWidth;

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

        inputHeightfieldResolutionPerChunk =
            Mathf.Max(
                1,
                inputHeightfieldResolutionPerChunk
            );

        GUILayout.Space(5f);

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
            "Saved Heightfield Resolution / Chunk",
            worldSettings.heightfieldResolutionPerChunk.ToString()
        );

        EditorGUILayout.LabelField(
            "Native Height Sample Spacing",
            (
                Mathf.Max(
                    0.01f,
                    worldSettings.chunkSize
                )
                /
                Mathf.Max(
                    1,
                    worldSettings.heightfieldResolutionPerChunk
                )
            ).ToString()
        );


        GUILayout.Space(5f);

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

        // =================================================
        // RESET GENERATED DATA
        // =================================================

        GUILayout.Space(10f);

        EditorGUILayout.HelpBox(
            "Reset Generated World deletes derived runtime " +
            "terrain data:\n\n" +
            "• Generated clipmap geometry\n" +
            "• Runtime heightmap tiles + manifest\n" +
            "• Collision meshes + runtime data\n" +
            "• WorldRoot scene hierarchy\n\n" +
            "Authoring data and configuration are preserved.",
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

    // =====================================================
    // CREATE
    // =====================================================

    private void CreateWorldSettingsAsset()
    {
        EnsureWorldSettingsFolderExists();

        WorldSettings existingSettings =
            AssetDatabase
                .LoadAssetAtPath<WorldSettings>(
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

        newSettings.heightfieldResolutionPerChunk =
            Mathf.Max(
                1,
                inputHeightfieldResolutionPerChunk
            );

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

    // =====================================================
    // LOAD
    // =====================================================

    private void LoadWorldSettingsIntoEditor()
    {
        if (worldSettings == null)
        {
            return;
        }

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

        inputHeightfieldResolutionPerChunk =
            Mathf.Max(
                1,
                worldSettings.heightfieldResolutionPerChunk
            );

        inputCollisionResolution =
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            );

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

    // =====================================================
    // UPDATE
    // =====================================================

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

        worldSettings.heightfieldResolutionPerChunk =
            Mathf.Max(
                1,
                inputHeightfieldResolutionPerChunk
            );

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
            $"Heightfield Resolution / Chunk: " +
            $"{worldSettings.heightfieldResolutionPerChunk}"
        );
    }
}
