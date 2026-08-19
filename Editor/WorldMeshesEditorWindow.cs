using UnityEditor;
using UnityEngine;

public class WorldMeshesEditorWindow : EditorWindow
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

    // Temporary editor input values.
    // The actual values are stored in WorldSettings.
    
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
    
    // =====================================================
    // COLLISION INPUTS
    // =====================================================

    [SerializeField]
    private int inputCollisionResolution = 64;

    // =====================================================
    // HEIGHT GENERATION INPUTS
    // =====================================================

    [SerializeField]
    private int inputHeightTileChunkSpan = 4;

    [SerializeField]
    private int inputHeightSeed = 12345;

    [SerializeField]
    private float inputHeightNoiseScale = 500f;

    [SerializeField]
    private float inputHeightBaseHeight = 0f;

    [SerializeField]
    private float inputHeightAmplitude = 100f;

    [SerializeField]
    private int inputHeightOctaves = 5;

    [SerializeField]
    private float inputHeightPersistence = 0.5f;

    [SerializeField]
    private float inputHeightLacunarity = 2f;

    // =====================================================
    // GRID DISPLAY
    // =====================================================

    // Purely visual editor-grid size.
    // This has no relationship to terrain chunk size.
        private const float GridCellPixelSize = 64f;

    // =====================================================
    // BASE MESH
    // =====================================================

    // [SerializeField]
    // private int baseMeshResolution = 128;

    // =====================================================
    // VIEWPORT
    // =====================================================

        private Vector2 panOffset =
            Vector2.zero;

        private bool isPanning;

    // =====================================================
    // SETTINGS SCROLL
    // =====================================================

        private Vector2 settingsScrollPosition =
            Vector2.zero;

    // =====================================================
    // LAYOUT
    // =====================================================

    private float contentPadding = 10f;
    private float columnGap = 10f;
    private float settingsMinWidth = 300f;

    // =====================================================
    // WINDOW
    // =====================================================

    [MenuItem("Tools/WorldMeshes")]
    public static void ShowWindow()
    {
        GetWindow<WorldMeshesEditorWindow>(
            "World Meshes"
        );
    }

    private void OnEnable()
    {
        // Attempt to automatically load the default
        // WorldSettings asset if one already exists.
        if (worldSettings == null)
        {
            worldSettings =
                AssetDatabase.LoadAssetAtPath<WorldSettings>(
                    DefaultWorldSettingsPath
                );
        }

        if (worldSettings != null)
        {
            LoadWorldSettingsIntoEditor();
        }
    }

    private void OnGUI()
    {
        Rect marker =
            GUILayoutUtility.GetRect(
                0f,
                0f
            );

        Rect contentArea =
            new Rect(
                contentPadding,
                marker.y + contentPadding,

                position.width
                    - contentPadding * 2f,

                position.height
                    - marker.y
                    - contentPadding * 2f
            );

        DrawMainLayout(
            contentArea
        );
    }

    // =====================================================
    // MAIN LAYOUT
    // =====================================================

    private void DrawMainLayout(
        Rect contentArea
    )
    {
        float maxViewportWidth =
            contentArea.width
            - columnGap
            - settingsMinWidth;

        float viewportSize =
            Mathf.Max(
                0f,
                Mathf.Min(
                    maxViewportWidth,
                    contentArea.height
                )
            );

        // -------------------------------------------------
        // Viewport
        // -------------------------------------------------

        Rect viewport =
            new Rect(
                contentArea.x,
                contentArea.y,
                viewportSize,
                viewportSize
            );

        // -------------------------------------------------
        // Settings area
        // -------------------------------------------------

        float settingsX =
            viewport.xMax
            + columnGap;

        float settingsWidth =
            contentArea.xMax
            - settingsX;

        Rect settingsArea =
            new Rect(
                settingsX,
                contentArea.y,
                settingsWidth,
                contentArea.height
            );

        // -------------------------------------------------
        // Grid viewport
        // -------------------------------------------------

        DrawChunkGrid(
            viewport
        );

        // -------------------------------------------------
        // Scrollable settings
        // -------------------------------------------------

        GUILayout.BeginArea(
            settingsArea
        );

        settingsScrollPosition =
            EditorGUILayout.BeginScrollView(
                settingsScrollPosition,
                false,
                false,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );

        // -------------------------------------------------
        // World settings
        // -------------------------------------------------

        DrawWorldSettings();

        GUILayout.Space(10f);

        // -------------------------------------------------
        // Generation state
        // -------------------------------------------------

        DrawGenerationStateSettings();

        GUILayout.Space(10f);

        // -------------------------------------------------
        // Clipmap
        // -------------------------------------------------

        DrawClipmapGenerationSettings();

        GUILayout.Space(10f);

        // -------------------------------------------------
        // Base mesh
        // -------------------------------------------------

        DrawBaseMeshSettings();

        GUILayout.Space(10f);

        // -------------------------------------------------
        // Chunk meshes
        // -------------------------------------------------

        DrawChunkMeshSettings();

        GUILayout.Space(10f);

        // -------------------------------------------------
        // Height generation
        // -------------------------------------------------

        DrawHeightGenerationSettings();

        GUILayout.Space(10f);

        // -------------------------------------------------
        // Height application
        // -------------------------------------------------

        DrawHeightApplicationSettings();

        GUILayout.Space(10f);

        // -------------------------------------------------
        // Collision generation
        // -------------------------------------------------

        DrawCollisionGenerationSettings();

        GUILayout.Space(10f);

        // -------------------------------------------------
        // World hierarchy
        // -------------------------------------------------

        DrawWorldHierarchySettings();

        GUILayout.Space(10f);

        // -------------------------------------------------
        // Runtime validation
        // -------------------------------------------------

        DrawRuntimeValidationSettings();

        EditorGUILayout.EndScrollView();

        GUILayout.EndArea();
    }

    // =====================================================
    // WORLD SETTINGS
    // =====================================================

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

    // =====================================================
    // CREATE WORLD SETTINGS
    // =====================================================

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
    
    // =====================================================
    // LOAD CLIPMAP SETTINGS INTO EDITOR
    // =====================================================

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

    // =====================================================
    // LOAD WORLD SETTINGS
    // =====================================================

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

    // =====================================================
    // UPDATE WORLD GRID
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

    // =====================================================
    // BASE MESH SETTINGS
    // =====================================================

    private void DrawBaseMeshSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Base Mesh",
            EditorStyles.boldLabel
        );

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "generating the base mesh.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        // -------------------------------------------------
        // Current generation settings
        // -------------------------------------------------

        EditorGUILayout.LabelField(
            "Chunk Size",
            worldSettings.chunkSize.ToString()
        );

        EditorGUILayout.LabelField(
            "LOD0 Resolution",
            worldSettings.lod0Resolution.ToString()
        );

        GUILayout.Space(5f);

        int vertexCount =
            (worldSettings.lod0Resolution + 1) *
            (worldSettings.lod0Resolution + 1);

        EditorGUILayout.HelpBox(
            $"Creates a {worldSettings.chunkSize} x " +
            $"{worldSettings.chunkSize} unit flat terrain mesh " +
            $"with {worldSettings.lod0Resolution} x " +
            $"{worldSettings.lod0Resolution} quads.\n\n" +
            $"Vertices: {vertexCount:N0}",
            MessageType.Info
        );

        GUILayout.Space(5f);

        // -------------------------------------------------
        // Generate
        // -------------------------------------------------

        if (
            GUILayout.Button(
                "Generate LOD0 Base Mesh",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainBaseMeshGenerator
                .GenerateBaseMesh(
                    worldSettings.chunkSize,
                    worldSettings.lod0Resolution
                );
        }

        GUILayout.EndVertical();
    }
    

    // =====================================================
    // WORLD HIERARCHY SETTINGS
    // =====================================================

    private void DrawWorldHierarchySettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "World Hierarchy",
            EditorStyles.boldLabel
        );

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "creating the world hierarchy.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        int totalChunks =
            worldSettings.gridWidth *
            worldSettings.gridHeight;

        int clipmapLevelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                1,
                10
            );

        int clipmapMeshObjects =
            1
            +
            Mathf.Max(
                0,
                clipmapLevelCount - 1
            )
            *
            2;

        EditorGUILayout.LabelField(
            "World Root",
            TerrainWorldHierarchyGenerator
                .WorldRootName
        );

        EditorGUILayout.LabelField(
            "World Grid",
            $"{worldSettings.gridWidth} x " +
            $"{worldSettings.gridHeight}"
        );

        EditorGUILayout.LabelField(
            "Preview Chunks",
            totalChunks.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Collision Chunks",
            totalChunks.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Clipmap Levels",
            clipmapLevelCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Clipmap Mesh Objects",
            clipmapMeshObjects.ToString()
        );

        GUILayout.Space(8f);

        EditorGUILayout.HelpBox(
            "Synchronizes three independent terrain " +
            "representations beneath WorldRoot:\n\n" +

            "Preview\n" +
            "Generated LOD0 terrain chunks used for editor " +
            "preview and future terrain editing.\n\n" +

            "Collision\n" +
            "Generated collision chunks containing direct " +
            "MeshCollider components. Collision chunks remain " +
            "disabled until required by runtime streaming.\n\n" +

            "Clipmap\n" +
            "The center, LOD rings, and transition stitch meshes " +
            "used by the runtime terrain renderer.\n\n" +

            "Legacy Chunk_x_z objects directly beneath WorldRoot " +
            "are removed automatically.",
            MessageType.Info
        );

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Sync World Hierarchy",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainWorldHierarchyGenerator
                .SyncWorldHierarchy(
                    worldSettings
                );
        }

        GUILayout.EndVertical();
    }
    
    // =====================================================
    // RUNTIME VALIDATION SETTINGS
    // =====================================================

    private void DrawRuntimeValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Runtime validation is development tooling only.\n\n" +
            "Height-cache validation performs a full GPU readback " +
            "and exact comparison against the currently loaded " +
            "source heightmap tiles.\n\n" +
            "It never runs automatically during normal streaming.",
            MessageType.Info
        );

        // =====================================================
        // PLAY MODE REQUIRED
        // =====================================================

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode before validating the runtime " +
                "height cache.",
                MessageType.Warning
            );

            EditorGUI.BeginDisabledGroup(
                true
            );

            GUILayout.Button(
                "Validate Current Height Cache",
                GUILayout.ExpandWidth(true)
            );

            EditorGUI.EndDisabledGroup();

            GUILayout.EndVertical();

            return;
        }

        // =====================================================
        // VALIDATOR
        // =====================================================

        TerrainHeightmapCacheValidator validator =
            FindRuntimeHeightmapCacheValidator();

        if (validator == null)
        {
            EditorGUILayout.HelpBox(
                "TerrainHeightmapCacheValidator was not found on " +
                "WorldRoot/Clipmap.\n\n" +
                "Exit Play Mode and run Sync World Hierarchy.",
                MessageType.Warning
            );

            EditorGUI.BeginDisabledGroup(
                true
            );

            GUILayout.Button(
                "Validate Current Height Cache",
                GUILayout.ExpandWidth(true)
            );

            EditorGUI.EndDisabledGroup();

            GUILayout.EndVertical();

            return;
        }

        // =====================================================
        // CURRENT STATUS
        // =====================================================

        if (validator.IsValidating)
        {
            EditorGUILayout.HelpBox(
                "Height-cache validation is currently running.",
                MessageType.Info
            );
        }
        else if (
            validator.HasValidatedCurrentCache
        )
        {
            if (validator.LastValidationPassed)
            {
                EditorGUILayout.HelpBox(
                    "The current height cache passed validation.",
                    MessageType.Info
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "The current height cache failed validation. " +
                    "See the Console for details.",
                    MessageType.Error
                );
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "The current height cache has not been manually " +
                "validated.",
                MessageType.None
            );
        }

        // =====================================================
        // VALIDATE
        // =====================================================

        EditorGUI.BeginDisabledGroup(
            validator.IsValidating
        );

        if (
            GUILayout.Button(
                "Validate Current Height Cache",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            validator.BeginValidation();

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.EndVertical();
    }

    // =====================================================
    // FIND RUNTIME HEIGHTMAP CACHE VALIDATOR
    // =====================================================

    private TerrainHeightmapCacheValidator
        FindRuntimeHeightmapCacheValidator()
    {
        GameObject worldRoot =
            GameObject.Find(
                TerrainWorldHierarchyGenerator
                    .WorldRootName
            );

        if (worldRoot == null)
        {
            return null;
        }

        Transform clipmapRoot =
            worldRoot.transform.Find(
                TerrainWorldHierarchyGenerator
                    .ClipmapRootName
            );

        if (clipmapRoot == null)
        {
            return null;
        }

        return
            clipmapRoot
                .GetComponent<TerrainHeightmapCacheValidator>();
    }
            
    // =====================================================
    // CHUNK MESH SETTINGS
    // =====================================================
    
    private void DrawChunkMeshSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Chunk Meshes",
            EditorStyles.boldLabel
        );

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "synchronizing chunk meshes.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        int totalChunks =
            worldSettings.gridWidth *
            worldSettings.gridHeight;

        EditorGUILayout.LabelField(
            "World Grid",
            $"{worldSettings.gridWidth} x " +
            $"{worldSettings.gridHeight}"
        );

        EditorGUILayout.LabelField(
            "Chunk Size",
            worldSettings.chunkSize.ToString()
        );

        EditorGUILayout.LabelField(
            "LOD0 Resolution",
            worldSettings.lod0Resolution.ToString()
        );

        EditorGUILayout.LabelField(
            "Required Meshes",
            totalChunks.ToString("N0")
        );

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Synchronizes generated chunk meshes with the " +
            "current WorldSettings and LOD0 base mesh.\n\n" +
            "If the chunk size or LOD0 resolution has changed " +
            "since the previous successful synchronization, " +
            "all existing required chunk meshes are rebuilt.",
            MessageType.Info
        );

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Sync Chunk Meshes",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainChunkMeshGenerator
                .SyncChunkMeshes(
                    worldSettings
                );
        }

        GUILayout.EndVertical();
    }
    
    // =====================================================
    // HEIGHT GENERATION SETTINGS
    // =====================================================

    // =====================================================
// HEIGHT GENERATION SETTINGS
// =====================================================

private void DrawHeightGenerationSettings()
{
    GUILayout.BeginVertical(
        EditorStyles.helpBox,
        GUILayout.ExpandWidth(true)
    );

    GUILayout.Label(
        "Height Generation",
        EditorStyles.boldLabel
    );

    // -------------------------------------------------
    // No WorldSettings
    // -------------------------------------------------

    if (worldSettings == null)
    {
        EditorGUILayout.HelpBox(
            "Assign or create WorldSettings before " +
            "configuring terrain height generation.",
            MessageType.Warning
        );

        GUILayout.EndVertical();

        return;
    }

    // -------------------------------------------------
    // Editable settings
    // -------------------------------------------------

    float oldLabelWidth =
        EditorGUIUtility.labelWidth;

    EditorGUIUtility.labelWidth =
        140f;

    inputHeightTileChunkSpan =
        EditorGUILayout.IntField(
            "Tile Chunk Span",
            inputHeightTileChunkSpan
        );

    inputHeightSeed =
        EditorGUILayout.IntField(
            "Seed",
            inputHeightSeed
        );

    inputHeightNoiseScale =
        EditorGUILayout.FloatField(
            "Noise Scale",
            inputHeightNoiseScale
        );

    inputHeightBaseHeight =
        EditorGUILayout.FloatField(
            "Base Height",
            inputHeightBaseHeight
        );

    inputHeightAmplitude =
        EditorGUILayout.FloatField(
            "Height Amplitude",
            inputHeightAmplitude
        );

    inputHeightOctaves =
        EditorGUILayout.IntSlider(
            "Octaves",
            inputHeightOctaves,
            1,
            12
        );

    inputHeightPersistence =
        EditorGUILayout.Slider(
            "Persistence",
            inputHeightPersistence,
            0f,
            1f
        );

    inputHeightLacunarity =
        EditorGUILayout.FloatField(
            "Lacunarity",
            inputHeightLacunarity
        );

    EditorGUIUtility.labelWidth =
        oldLabelWidth;

    // -------------------------------------------------
    // Clamp input
    // -------------------------------------------------

    inputHeightTileChunkSpan =
        Mathf.Max(
            1,
            inputHeightTileChunkSpan
        );

    inputHeightNoiseScale =
        Mathf.Max(
            0.0001f,
            inputHeightNoiseScale
        );

    inputHeightAmplitude =
        Mathf.Max(
            0f,
            inputHeightAmplitude
        );

    inputHeightOctaves =
        Mathf.Clamp(
            inputHeightOctaves,
            1,
            12
        );

    inputHeightPersistence =
        Mathf.Clamp01(
            inputHeightPersistence
        );

    inputHeightLacunarity =
        Mathf.Max(
            1f,
            inputHeightLacunarity
        );

    // -------------------------------------------------
    // Derived layout
    // -------------------------------------------------

    GUILayout.Space(8f);

    GUILayout.Label(
        "Derived Heightmap Layout",
        EditorStyles.boldLabel
    );

    int tileChunkSpan =
        inputHeightTileChunkSpan;

    float tileWorldSize =
        worldSettings.chunkSize *
        tileChunkSpan;

    int heightTileGridWidth =
        Mathf.CeilToInt(
            (float)worldSettings.gridWidth /
            tileChunkSpan
        );

    int heightTileGridHeight =
        Mathf.CeilToInt(
            (float)worldSettings.gridHeight /
            tileChunkSpan
        );

    int samplesPerSide =
        tileChunkSpan *
        worldSettings.lod0Resolution
        +
        1;

    int totalHeightTiles =
        heightTileGridWidth *
        heightTileGridHeight;

    EditorGUILayout.LabelField(
        "Tile World Size",
        $"{tileWorldSize} x " +
        $"{tileWorldSize}"
    );

    EditorGUILayout.LabelField(
        "Height Tile Grid",
        $"{heightTileGridWidth} x " +
        $"{heightTileGridHeight}"
    );

    EditorGUILayout.LabelField(
        "Samples Per Tile",
        $"{samplesPerSide} x " +
        $"{samplesPerSide}"
    );

    EditorGUILayout.LabelField(
        "Total Height Tiles",
        totalHeightTiles.ToString("N0")
    );

    // -------------------------------------------------
    // Partial edge tiles
    // -------------------------------------------------

    bool hasPartialEdgeTiles =
        worldSettings.gridWidth %
            tileChunkSpan != 0
        ||
        worldSettings.gridHeight %
            tileChunkSpan != 0;

    if (hasPartialEdgeTiles)
    {
        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "The world grid is not evenly divisible by " +
            "the Tile Chunk Span.\n\n" +

            "The final heightmap tile along one or both " +
            "axes will extend beyond the world grid. " +
            "Unused samples will simply remain outside " +
            "the playable world.",
            MessageType.Info
        );
    }

    // -------------------------------------------------
    // Explanation
    // -------------------------------------------------

    GUILayout.Space(8f);

    EditorGUILayout.HelpBox(
        "Each heightmap tile covers Tile Chunk Span x " +
        "Tile Chunk Span terrain chunks.\n\n" +

        "Noise is generated using global world " +
        "coordinates so neighboring heightmap tiles " +
        "share exactly the same boundary samples.",
        MessageType.Info
    );

    // -------------------------------------------------
    // Save settings
    // -------------------------------------------------

    GUILayout.Space(5f);

    if (
        GUILayout.Button(
            "Update Height Settings",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        UpdateHeightSettings();
    }

    if (
        GUILayout.Button(
            "Reload Height Settings",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        LoadWorldSettingsIntoEditor();
    }

    // =====================================================
    // GENERATION
    // =====================================================

    GUILayout.Space(10f);

    EditorGUILayout.HelpBox(
        "Heightmap generation uses the SAVED values " +
        "in WorldSettings.\n\n" +

        "Update the settings before generating if " +
        "you have changed any values.",
        MessageType.Warning
    );

    if (
        GUILayout.Button(
            "Generate / Regenerate Heightmaps",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        TerrainHeightmapGenerator
            .GenerateHeightmaps(
                worldSettings
            );
    }

    // =====================================================
    // VALIDATION
    // =====================================================

    GUILayout.Space(5f);

    if (
        GUILayout.Button(
            "Validate Heightmap Tiles",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        TerrainHeightmapValidator
            .ValidateHeightmaps(
                worldSettings
            );
    }

    // =====================================================
    // RUNTIME STREAMING PREPARATION
    // =====================================================

    GUILayout.Space(10f);

    GUILayout.Label(
        "Runtime Streaming",
        EditorStyles.boldLabel
    );

    TerrainGenerationStateUtility.GenerationStatus
        heightmapStatus =
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                );

    bool heightmapsCurrent =
        heightmapStatus ==
        TerrainGenerationStateUtility
            .GenerationStatus.Current;

    EditorGUILayout.LabelField(
        "Heightmap State",
        TerrainGenerationStateUtility
            .GetStatusLabel(
                heightmapStatus
            )
    );

    GUILayout.Space(5f);

    EditorGUILayout.HelpBox(
        "Preparing the heightmap tiles for runtime registers " +
        "the existing generated Texture2D assets with Unity " +
        "Addressables.\n\n" +

        "The heightmap data is not regenerated or modified. " +
        "Each HeightTile_x_z asset is assigned a deterministic " +
        "runtime address so individual tiles can later be " +
        "loaded and released by the terrain streaming system.",
        MessageType.Info
    );

    // -------------------------------------------------
    // Invalid / outdated heightmaps
    // -------------------------------------------------

    if (!heightmapsCurrent)
    {
        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "The generated heightmaps are not current.\n\n" +

            "Generate or regenerate the heightmaps before " +
            "preparing them for runtime streaming.",
            MessageType.Warning
        );
    }

    // -------------------------------------------------
    // Prepare Addressables
    // -------------------------------------------------

    EditorGUI.BeginDisabledGroup(
        !heightmapsCurrent
    );

    if (
        GUILayout.Button(
            "Prepare Heightmap Tiles For Runtime",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        TerrainHeightmapAddressablesUtility
            .PrepareHeightmapTilesForRuntime();
    }

    EditorGUI.EndDisabledGroup();

    // -------------------------------------------------
    // Addressable layout
    // -------------------------------------------------

    GUILayout.Space(5f);

    EditorGUILayout.LabelField(
        "Addressables Group",
        TerrainHeightmapAddressablesUtility
            .HeightmapAddressablesGroupName
    );

    EditorGUILayout.LabelField(
        "Address Pattern",
        TerrainHeightmapManifest
            .HeightTileAddressPrefix
        +
        "_X_Z"
    );

    // =====================================================
    // OUTPUT
    // =====================================================

    GUILayout.Space(10f);

    EditorGUILayout.LabelField(
        "Heightmap Output Folder",
        TerrainHeightmapGenerator
            .HeightmapTileFolder
    );

    GUILayout.EndVertical();
}
    
    // =====================================================
    // UPDATE CLIPMAP SETTINGS
    // =====================================================

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

    // =====================================================
    // UPDATE HEIGHT GENERATION SETTINGS
    // =====================================================

    private void UpdateHeightSettings()
    {
        if (worldSettings == null)
        {
            return;
        }

        Undo.RecordObject(
            worldSettings,
            "Update Height Settings"
        );

        // -------------------------------------------------
        // Store height settings
        // -------------------------------------------------

        worldSettings.heightTileChunkSpan =
            Mathf.Max(
                1,
                inputHeightTileChunkSpan
            );

        worldSettings.heightSeed =
            inputHeightSeed;

        worldSettings.heightNoiseScale =
            Mathf.Max(
                0.0001f,
                inputHeightNoiseScale
            );

        worldSettings.heightBaseHeight =
            inputHeightBaseHeight;

        worldSettings.heightAmplitude =
            Mathf.Max(
                0f,
                inputHeightAmplitude
            );

        worldSettings.heightOctaves =
            Mathf.Clamp(
                inputHeightOctaves,
                1,
                12
            );

        worldSettings.heightPersistence =
            Mathf.Clamp01(
                inputHeightPersistence
            );

        worldSettings.heightLacunarity =
            Mathf.Max(
                1f,
                inputHeightLacunarity
            );

        // -------------------------------------------------
        // Save WorldSettings
        // -------------------------------------------------

        EditorUtility.SetDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );

        Repaint();

        // -------------------------------------------------
        // Result
        // -------------------------------------------------

        Debug.Log(
            "Height generation settings updated.\n\n" +

            $"Tile Chunk Span: " +
            $"{worldSettings.heightTileChunkSpan}\n" +

            $"Tile World Size: " +
            $"{worldSettings.HeightTileWorldSize}\n" +

            $"Height Tile Grid: " +
            $"{worldSettings.HeightTileGridWidth} x " +
            $"{worldSettings.HeightTileGridHeight}\n" +

            $"Samples Per Tile: " +
            $"{worldSettings.HeightTileSamplesPerSide} x " +
            $"{worldSettings.HeightTileSamplesPerSide}\n" +

            $"Total Height Tiles: " +
            $"{worldSettings.HeightTileCount}\n\n" +

            $"Seed: " +
            $"{worldSettings.heightSeed}\n" +

            $"Noise Scale: " +
            $"{worldSettings.heightNoiseScale}\n" +

            $"Base Height: " +
            $"{worldSettings.heightBaseHeight}\n" +

            $"Height Amplitude: " +
            $"{worldSettings.heightAmplitude}\n" +

            $"Octaves: " +
            $"{worldSettings.heightOctaves}\n" +

            $"Persistence: " +
            $"{worldSettings.heightPersistence}\n" +

            $"Lacunarity: " +
            $"{worldSettings.heightLacunarity}"
        );
    }
    
    // =====================================================
// CLIPMAP GENERATION SETTINGS
// =====================================================

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
            worldSettings.lod0Resolution
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


    // =====================================================
    // HEIGHT APPLICATION SETTINGS
    // =====================================================

    private void DrawHeightApplicationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Height Application",
            EditorStyles.boldLabel
        );

        // -------------------------------------------------
        // No WorldSettings
        // -------------------------------------------------

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "applying or validating terrain height.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        // -------------------------------------------------
        // Current layout
        // -------------------------------------------------

        int totalChunks =
            worldSettings.gridWidth *
            worldSettings.gridHeight;

        int samplesPerChunkSide =
            worldSettings.lod0Resolution +
            1;

        EditorGUILayout.LabelField(
            "Terrain Chunks",
            totalChunks.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Mesh Resolution",
            $"{worldSettings.lod0Resolution} x " +
            $"{worldSettings.lod0Resolution}"
        );

        EditorGUILayout.LabelField(
            "Vertices Per Side",
            samplesPerChunkSide.ToString()
        );

        EditorGUILayout.LabelField(
            "Height Tile Grid",
            $"{worldSettings.HeightTileGridWidth} x " +
            $"{worldSettings.HeightTileGridHeight}"
        );

        // -------------------------------------------------
        // Apply
        // -------------------------------------------------

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Applies the generated heightmap data to every " +
            "LOD0 terrain chunk mesh.\n\n" +

            "Vertex normals are calculated from the global " +
            "heightfield so neighboring chunks use matching " +
            "normals along shared boundaries.",
            MessageType.Info
        );

        if (
            GUILayout.Button(
                "Apply Heightmaps to Chunk Meshes",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainHeightApplicator
                .ApplyHeightmaps(
                    worldSettings
                );
        }

        // -------------------------------------------------
        // Seam validation
        // -------------------------------------------------

        GUILayout.Space(10f);

        EditorGUILayout.HelpBox(
            "Validates every shared terrain chunk edge.\n\n" +

            "The validator compares corresponding boundary " +
            "vertex positions, heights, and normals between " +
            "neighboring chunk mesh assets.",
            MessageType.Info
        );

        if (
            GUILayout.Button(
                "Validate Chunk Mesh Seams",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainMeshSeamValidator
                .ValidateMeshSeams(
                    worldSettings
                );
        }

        GUILayout.EndVertical();
    }

    // =====================================================
    // GENERATION STATE
    // =====================================================

    private void DrawGenerationStateSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Generation State",
            EditorStyles.boldLabel
        );

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings to view " +
                "terrain generation state.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        // -------------------------------------------------
        // Current states
        // -------------------------------------------------

        TerrainGenerationStateUtility.GenerationStatus
            chunkStatus =
                TerrainGenerationStateUtility
                    .GetChunkMeshStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            heightmapStatus =
                TerrainGenerationStateUtility
                    .GetHeightmapStatus(
                        worldSettings
                    );

        TerrainGenerationStateUtility.GenerationStatus
            applicationStatus =
                TerrainGenerationStateUtility
                    .GetHeightApplicationStatus(
                        worldSettings
                    );
        
        TerrainGenerationStateUtility.GenerationStatus
            collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        worldSettings
                    );

        // -------------------------------------------------
        // Display
        // -------------------------------------------------

        EditorGUILayout.LabelField(
            "Chunk Meshes",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    chunkStatus
                )
        );

        EditorGUILayout.LabelField(
            "Heightmaps",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    heightmapStatus
                )
        );

        string applicationLabel =
            applicationStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.NotGenerated

                ? "Not Applied"

                : TerrainGenerationStateUtility
                    .GetStatusLabel(
                        applicationStatus
                    );

        EditorGUILayout.LabelField(
            "Height Applied",
            applicationLabel
        );
        
        EditorGUILayout.LabelField(
            "Collision Meshes",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    collisionStatus
                )
        );

        // -------------------------------------------------
        // Revision information
        // -------------------------------------------------

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Chunk Revision",
            worldSettings
                .chunkMeshGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Heightmap Revision",
            worldSettings
                .heightmapGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Applied Chunk Revision",
            worldSettings
                .appliedChunkMeshGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Applied Height Revision",
            worldSettings
                .appliedHeightmapGenerationRevision
                .ToString()
        );
        
        EditorGUILayout.LabelField(
            "Collision Revision",
            worldSettings
                .collisionMeshGenerationRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Collision Height Source",
            worldSettings
                .collisionSourceHeightmapGenerationRevision
                .ToString()
        );

        // -------------------------------------------------
        // Explanation
        // -------------------------------------------------

        GUILayout.Space(5f);

        if (
            applicationStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.OutOfDate
        )
        {
            EditorGUILayout.HelpBox(
                "The generated terrain meshes do not contain " +
                "the current combination of chunk-mesh and " +
                "heightmap revisions.\n\n" +

                "Apply the current heightmaps to the chunk " +
                "meshes again.",
                MessageType.Warning
            );
        }
        else if (
            chunkStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.OutOfDate
        )
        {
            EditorGUILayout.HelpBox(
                "Chunk meshes are out of date with the " +
                "current WorldSettings or LOD0 base mesh.",
                MessageType.Warning
            );
        }
        else if (
            heightmapStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.OutOfDate
        )
        {
            EditorGUILayout.HelpBox(
                "Heightmaps are out of date with the current " +
                "world or height-generation settings.",
                MessageType.Warning
            );
        }
        else if (
            chunkStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            heightmapStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
            &&
            applicationStatus ==
                TerrainGenerationStateUtility
                    .GenerationStatus.Current
        )
        {
            EditorGUILayout.HelpBox(
                "The LOD0 terrain generation pipeline is " +
                "current.",
                MessageType.Info
            );
        }

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Generation State tracks which successful " +
            "generation revisions produced the current " +
            "terrain.\n\n" +

            "The validation tools remain responsible for " +
            "checking the actual generated asset contents.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
    
    // =====================================================
    // COLLISION GENERATION SETTINGS
    // =====================================================

    // =====================================================
// COLLISION GENERATION SETTINGS
// =====================================================

// =====================================================
// COLLISION GENERATION SETTINGS
// =====================================================

private void DrawCollisionGenerationSettings()
{
    GUILayout.BeginVertical(
        EditorStyles.helpBox,
        GUILayout.ExpandWidth(true)
    );

    GUILayout.Label(
        "Collision Generation",
        EditorStyles.boldLabel
    );

    if (worldSettings == null)
    {
        EditorGUILayout.HelpBox(
            "Assign or create WorldSettings before " +
            "generating terrain collision.",
            MessageType.Warning
        );

        GUILayout.EndVertical();

        return;
    }

    // -------------------------------------------------
    // Editable settings
    // -------------------------------------------------

    float oldLabelWidth =
        EditorGUIUtility.labelWidth;

    EditorGUIUtility.labelWidth =
        140f;

    inputCollisionResolution =
        EditorGUILayout.IntField(
            "Collision Resolution",
            inputCollisionResolution
        );

    inputCollisionResolution =
        Mathf.Max(
            1,
            inputCollisionResolution
        );

    EditorGUIUtility.labelWidth =
        oldLabelWidth;

    // -------------------------------------------------
    // Saved settings
    // -------------------------------------------------

    GUILayout.Space(5f);

    EditorGUILayout.LabelField(
        "Saved Resolution",
        worldSettings
            .collisionResolution
            .ToString()
    );

    // -------------------------------------------------
    // Derived values
    // -------------------------------------------------

    int lod0Resolution =
        Mathf.Max(
            1,
            worldSettings.lod0Resolution
        );

    bool resolutionValid =
        inputCollisionResolution <=
            lod0Resolution
        &&
        lod0Resolution %
            inputCollisionResolution == 0;

    if (resolutionValid)
    {
        int heightSampleStep =
            lod0Resolution /
            inputCollisionResolution;

        int verticesPerSide =
            inputCollisionResolution +
            1;

        int totalVertices =
            verticesPerSide *
            verticesPerSide;

        int triangleCount =
            inputCollisionResolution *
            inputCollisionResolution *
            2;

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Height Sample Step",
            heightSampleStep.ToString()
        );

        EditorGUILayout.LabelField(
            "Vertices Per Side",
            verticesPerSide.ToString()
        );

        EditorGUILayout.LabelField(
            "Vertices Per Mesh",
            totalVertices.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Triangles Per Mesh",
            triangleCount.ToString("N0")
        );
    }
    else
    {
        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "LOD0 Resolution must be evenly divisible " +
            "by Collision Resolution.\n\n" +

            $"Current LOD0 Resolution: " +
            $"{lod0Resolution}",

            MessageType.Warning
        );
    }

    // -------------------------------------------------
    // Explanation
    // -------------------------------------------------

    GUILayout.Space(8f);

    EditorGUILayout.HelpBox(
        "Collision meshes are generated directly from " +
        "the current heightmap data.\n\n" +

        "A lower collision resolution reduces physics " +
        "geometry while keeping collision vertices exactly " +
        "aligned to existing heightmap samples.",
        MessageType.Info
    );

    // -------------------------------------------------
    // Update / reload
    // -------------------------------------------------

    GUILayout.Space(5f);

    if (
        GUILayout.Button(
            "Update Collision Settings",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        UpdateCollisionSettings();
    }

    if (
        GUILayout.Button(
            "Reload Collision Settings",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        inputCollisionResolution =
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            );

        Repaint();
    }

    // =====================================================
    // GENERATION
    // =====================================================

    GUILayout.Space(10f);

    if (
        GUILayout.Button(
            "Generate / Regenerate Collision Meshes",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        TerrainCollisionMeshGenerator
            .GenerateCollisionMeshes(
                worldSettings
            );
    }

    GUILayout.Space(5f);

    EditorGUILayout.LabelField(
        "Output Folder",
        TerrainCollisionMeshGenerator
            .CollisionMeshFolder
    );

    // =====================================================
    // SEAM VALIDATION
    // =====================================================

    GUILayout.Space(10f);

    EditorGUILayout.HelpBox(
        "Validates every shared collision-mesh edge.\n\n" +

        "Corresponding boundary vertices are compared " +
        "between neighboring chunks after accounting for " +
        "their world-grid positions.\n\n" +

        "No generated assets are modified.",
        MessageType.Info
    );

    if (
        GUILayout.Button(
            "Validate Collision Mesh Seams",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        TerrainCollisionSeamValidator
            .ValidateCollisionSeams(
                worldSettings
            );
    }

    // =====================================================
    // PHYSICS VALIDATION
    // =====================================================

    GUILayout.Space(10f);

    EditorGUILayout.HelpBox(
        "Validates the collision hierarchy and performs " +
        "actual MeshCollider raycasts against the generated " +
        "collision meshes.\n\n" +

        "Terrain chunk roots may remain disabled. The " +
        "validator does not activate or modify them.\n\n" +

        "Physics queries are performed using a temporary " +
        "hidden MeshCollider which is destroyed when " +
        "validation finishes.",
        MessageType.Info
    );

    if (
        GUILayout.Button(
            "Validate Collision Physics",
            GUILayout.ExpandWidth(true)
        )
    )
    {
        TerrainCollisionPhysicsValidator
            .ValidateCollisionPhysics(
                worldSettings
            );
    }

    GUILayout.EndVertical();
}
    
    // =====================================================
    // UPDATE COLLISION SETTINGS
    // =====================================================

    private void UpdateCollisionSettings()
    {
        if (worldSettings == null)
        {
            return;
        }

        int collisionResolution =
            Mathf.Max(
                1,
                inputCollisionResolution
            );

        int lod0Resolution =
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            );

        if (
            collisionResolution >
            lod0Resolution
        )
        {
            EditorUtility.DisplayDialog(
                "Invalid Collision Resolution",

                "Collision Resolution cannot be greater " +
                "than LOD0 Resolution.",

                "OK"
            );

            return;
        }

        if (
            lod0Resolution %
            collisionResolution != 0
        )
        {
            EditorUtility.DisplayDialog(
                "Invalid Collision Resolution",

                "LOD0 Resolution must be evenly divisible " +
                "by Collision Resolution.\n\n" +

                $"LOD0 Resolution: " +
                $"{lod0Resolution}\n" +

                $"Collision Resolution: " +
                $"{collisionResolution}",

                "OK"
            );

            return;
        }

        Undo.RecordObject(
            worldSettings,
            "Update Collision Settings"
        );

        worldSettings.collisionResolution =
            collisionResolution;

        EditorUtility.SetDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );

        Repaint();

        Debug.Log(
            "Collision settings updated.\n\n" +

            $"Collision Resolution: " +
            $"{worldSettings.collisionResolution}\n" +

            $"Height Sample Step: " +
            $"{worldSettings.lod0Resolution / worldSettings.collisionResolution}"
        );
    }

    // =====================================================
    // GRID VIEWPORT
    // =====================================================

    private void DrawChunkGrid(
        Rect viewport
    )
    {
        EditorGUI.DrawRect(
            viewport,
            new Color(
                0.12f,
                0.12f,
                0.12f
            )
        );

        HandlePanning(
            viewport
        );

        GUI.BeginClip(
            viewport
        );

        Rect localViewport =
            new Rect(
                0f,
                0f,
                viewport.width,
                viewport.height
            );

        if (worldSettings != null)
        {
            DrawVisibleGrid(
                localViewport
            );
        }
        else
        {
            DrawMissingWorldSettingsMessage(
                localViewport
            );
        }

        GUI.EndClip();
    }

    private void DrawMissingWorldSettingsMessage(
        Rect viewport
    )
    {
        GUIStyle style =
            new GUIStyle(
                EditorStyles.centeredGreyMiniLabel
            );

        style.alignment =
            TextAnchor.MiddleCenter;

        GUI.Label(
            viewport,
            "Assign or create a WorldSettings asset.",
            style
        );
    }

    // =====================================================
    // PANNING
    // =====================================================

    private void HandlePanning(
        Rect viewport
    )
    {
        Event e =
            Event.current;

        if (
            !viewport.Contains(
                e.mousePosition
            )
        )
        {
            return;
        }

        if (
            e.type ==
                EventType.MouseDown &&
            e.button == 2
        )
        {
            isPanning = true;

            e.Use();
        }

        if (
            e.type ==
                EventType.MouseUp &&
            e.button == 2
        )
        {
            isPanning = false;

            e.Use();
        }

        if (
            e.type ==
                EventType.MouseDrag &&
            isPanning
        )
        {
            panOffset +=
                e.delta;

            Repaint();

            e.Use();
        }
    }

    // =====================================================
    // GRID DRAWING
    // =====================================================

    private void DrawVisibleGrid(
        Rect viewport
    )
    {
        if (worldSettings == null)
        {
            return;
        }

        // The viewport grid now gets its dimensions
        // DIRECTLY from WorldSettings.
        int gridWidth =
            Mathf.Max(
                1,
                worldSettings.gridWidth
            );

        int gridHeight =
            Mathf.Max(
                1,
                worldSettings.gridHeight
            );

        Handles.BeginGUI();

        float gridPixelWidth =
            gridWidth
            * GridCellPixelSize;

        float gridPixelHeight =
            gridHeight
            * GridCellPixelSize;

        // -------------------------
        // Visible grid range
        // -------------------------

        int minX =
            Mathf.Max(
                0,
                Mathf.FloorToInt(
                    -panOffset.x
                    / GridCellPixelSize
                )
            );

        int maxX =
            Mathf.Min(
                gridWidth,
                Mathf.CeilToInt(
                    (
                        viewport.width
                        - panOffset.x
                    )
                    / GridCellPixelSize
                )
            );

        int minY =
            Mathf.Max(
                0,
                Mathf.FloorToInt(
                    -panOffset.y
                    / GridCellPixelSize
                )
            );

        int maxY =
            Mathf.Min(
                gridHeight,
                Mathf.CeilToInt(
                    (
                        viewport.height
                        - panOffset.y
                    )
                    / GridCellPixelSize
                )
            );

        // -------------------------
        // Vertical lines
        // -------------------------

        for (
            int x = minX;
            x <= maxX;
            x++
        )
        {
            float xPosition =
                panOffset.x
                + x * GridCellPixelSize;

            float yStart =
                Mathf.Max(
                    0f,
                    panOffset.y
                );

            float yEnd =
                Mathf.Min(
                    viewport.height,
                    panOffset.y
                    + gridPixelHeight
                );

            Handles.DrawLine(
                new Vector2(
                    xPosition,
                    yStart
                ),
                new Vector2(
                    xPosition,
                    yEnd
                )
            );
        }

        // -------------------------
        // Horizontal lines
        // -------------------------

        for (
            int y = minY;
            y <= maxY;
            y++
        )
        {
            float yPosition =
                panOffset.y
                + y * GridCellPixelSize;

            float xStart =
                Mathf.Max(
                    0f,
                    panOffset.x
                );

            float xEnd =
                Mathf.Min(
                    viewport.width,
                    panOffset.x
                    + gridPixelWidth
                );

            Handles.DrawLine(
                new Vector2(
                    xStart,
                    yPosition
                ),
                new Vector2(
                    xEnd,
                    yPosition
                )
            );
        }

        Handles.EndGUI();
    }
}