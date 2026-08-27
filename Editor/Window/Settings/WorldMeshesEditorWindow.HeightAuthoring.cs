using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    // =====================================================
    // HEIGHT AUTHORING INPUTS
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
    // TERRAIN AUTHORING DATA
    // =====================================================

    private const string DefaultTerrainAuthoringDataPath =
        WorldMeshesPaths.TerrainAuthoringDataAssetPath;

    [SerializeField]
    private TerrainAuthoringData terrainAuthoringData;

    [SerializeField]
    private TerrainHeightSourceMode inputHeightSourceMode =
        TerrainHeightSourceMode.Procedural;

    [SerializeField]
    private float inputFlatHeight =
        0f;

    [SerializeField]
    private Texture2D inputImportedHeightmap;

    private void LoadTerrainAuthoringDataIntoEditor()
    {
        if (terrainAuthoringData == null)
        {
            return;
        }

        inputHeightSourceMode =
            terrainAuthoringData.sourceMode;

        inputFlatHeight =
            terrainAuthoringData.flatHeight;

        inputImportedHeightmap =
            terrainAuthoringData.importedHeightmap;

        Repaint();
    }

    private void DrawHeightAuthoringSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Height Authoring",
            EditorStyles.boldLabel
        );

        // =================================================
        // WORLD SETTINGS
        // =================================================

        if (worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "Assign or create WorldSettings before " +
                "configuring terrain height authoring.",
                MessageType.Warning
            );

            GUILayout.EndVertical();

            return;
        }

        // =================================================
        // AUTHORING DATA ASSET
        // =================================================

        EditorGUI.BeginChangeCheck();

        TerrainAuthoringData selectedAuthoringData =
            (TerrainAuthoringData)
            EditorGUILayout.ObjectField(
                "Authoring Data",
                terrainAuthoringData,
                typeof(TerrainAuthoringData),
                false
            );

        if (EditorGUI.EndChangeCheck())
        {
            terrainAuthoringData =
                selectedAuthoringData;

            if (terrainAuthoringData != null)
            {
                LoadTerrainAuthoringDataIntoEditor();
            }

            Repaint();
        }

        if (terrainAuthoringData == null)
        {
            EditorGUILayout.HelpBox(
                "No TerrainAuthoringData asset is assigned.",
                MessageType.Warning
            );

            if (
                GUILayout.Button(
                    "Create Terrain Authoring Data",
                    GUILayout.ExpandWidth(true)
                )
            )
            {
                CreateTerrainAuthoringDataAsset();
            }

            GUILayout.EndVertical();

            return;
        }

        // =================================================
        // HEIGHTFIELD LAYOUT
        // =================================================

        GUILayout.Space(8f);

        GUILayout.Label(
            "Heightfield Layout",
            EditorStyles.boldLabel
        );

        float oldLabelWidth =
            EditorGUIUtility.labelWidth;

        EditorGUIUtility.labelWidth =
            150f;

        inputHeightTileChunkSpan =
            EditorGUILayout.IntField(
                "Tile Chunk Span",
                inputHeightTileChunkSpan
            );

        inputHeightTileChunkSpan =
            Mathf.Max(
                1,
                inputHeightTileChunkSpan
            );

        // =================================================
        // HEIGHT SOURCE
        // =================================================

        GUILayout.Space(8f);

        GUILayout.Label(
            "Height Source",
            EditorStyles.boldLabel
        );

        inputHeightSourceMode =
            (TerrainHeightSourceMode)
            EditorGUILayout.EnumPopup(
                "Source",
                inputHeightSourceMode
            );

        // =================================================
        // SOURCE-SPECIFIC SETTINGS
        // =================================================

        switch (inputHeightSourceMode)
        {
            // =============================================
            // FLAT
            // =============================================

            case TerrainHeightSourceMode.Flat:
            {
                GUILayout.Space(5f);

                GUILayout.Label(
                    "Flat Settings",
                    EditorStyles.boldLabel
                );

                inputFlatHeight =
                    EditorGUILayout.FloatField(
                        "Height",
                        inputFlatHeight
                    );

                EditorGUILayout.HelpBox(
                    "Initializes every authoring height sample " +
                    "to the specified world-space height.",
                    MessageType.Info
                );

                break;
            }

            // =============================================
            // PROCEDURAL
            // =============================================

            case TerrainHeightSourceMode.Procedural:
            {
                GUILayout.Space(5f);

                GUILayout.Label(
                    "Procedural Settings",
                    EditorStyles.boldLabel
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

                EditorGUILayout.HelpBox(
                    "Procedural initialization uses the existing " +
                    "global-coordinate Perlin/fBM generator.",
                    MessageType.Info
                );

                break;
            }

            // =============================================
            // IMPORTED
            // =============================================

            case TerrainHeightSourceMode.Imported:
            {
                GUILayout.Space(5f);

                GUILayout.Label(
                    "Imported Settings",
                    EditorStyles.boldLabel
                );

                inputImportedHeightmap =
                    (Texture2D)
                    EditorGUILayout.ObjectField(
                        "Heightmap",
                        inputImportedHeightmap,
                        typeof(Texture2D),
                        false
                    );

                EditorGUILayout.HelpBox(
                    "Imported heightfield initialization is not " +
                    "implemented yet. Selecting this mode will not " +
                    "modify the existing authoring heightfield.",
                    MessageType.Info
                );

                break;
            }
        }

        EditorGUIUtility.labelWidth =
            oldLabelWidth;

        // =================================================
        // DERIVED HEIGHTFIELD LAYOUT
        // =================================================

        DrawDerivedHeightfieldLayout();

        // =================================================
        // SAVE / RELOAD SETTINGS
        // =================================================

        GUILayout.Space(8f);

        if (
            GUILayout.Button(
                "Update Height Authoring Settings",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            UpdateHeightAuthoringSettings();
        }

        if (
            GUILayout.Button(
                "Reload Height Authoring Settings",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            LoadWorldSettingsIntoEditor();
            LoadTerrainAuthoringDataIntoEditor();
        }

        // =================================================
        // INITIALIZATION
        // =================================================

        GUILayout.Space(10f);

        GUILayout.Label(
            "Authoring Heightfield",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Authoring Revision",
            terrainAuthoringData
                .authoringRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Output Folder",
            TerrainAuthoringHeightInitializer
                .AuthoringHeightTileFolder
        );

        EditorGUILayout.HelpBox(
            "Initializing the authoring heightfield replaces the " +
            "current committed authoring height data.\n\n" +

            "Flat and Procedural initialization are implemented. " +
            "Imported initialization will be added later.",
            MessageType.Warning
        );

        bool importedMode =
            inputHeightSourceMode ==
            TerrainHeightSourceMode.Imported;

        EditorGUI.BeginDisabledGroup(
            importedMode
        );

        if (
            GUILayout.Button(
                terrainAuthoringData.authoringRevision > 0
                    ? "Reinitialize Authoring Heightfield"
                    : "Initialize Authoring Heightfield",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            InitializeAuthoringHeightfield();
        }

        EditorGUI.EndDisabledGroup();

        // =================================================
        // CURRENT RUNTIME HEIGHTMAP PIPELINE
        // =================================================

        DrawCurrentRuntimeHeightmapPipeline();

        GUILayout.EndVertical();
    }

    private void CreateTerrainAuthoringDataAsset()
    {
        EnsureWorldSettingsFolderExists();

        TerrainAuthoringData existingData =
            AssetDatabase
                .LoadAssetAtPath<TerrainAuthoringData>(
                    DefaultTerrainAuthoringDataPath
                );

        // -------------------------------------------------
        // Load existing asset if one already exists
        // -------------------------------------------------

        if (existingData != null)
        {
            terrainAuthoringData =
                existingData;

            LoadTerrainAuthoringDataIntoEditor();

            Selection.activeObject =
                terrainAuthoringData;

            Debug.Log(
                "Loaded existing TerrainAuthoringData:\n" +
                DefaultTerrainAuthoringDataPath
            );

            Repaint();

            return;
        }

        // -------------------------------------------------
        // Create new asset
        // -------------------------------------------------

        TerrainAuthoringData newData =
            ScriptableObject
                .CreateInstance<TerrainAuthoringData>();

        newData.sourceMode =
            inputHeightSourceMode;

        newData.flatHeight =
            inputFlatHeight;

        newData.importedHeightmap =
            inputImportedHeightmap;

        // -------------------------------------------------
        // Save asset
        // -------------------------------------------------

        AssetDatabase.CreateAsset(
            newData,
            DefaultTerrainAuthoringDataPath
        );

        AssetDatabase.SaveAssets();

        terrainAuthoringData =
            newData;

        LoadTerrainAuthoringDataIntoEditor();

        Selection.activeObject =
            terrainAuthoringData;

        Debug.Log(
            "Created TerrainAuthoringData:\n" +
            DefaultTerrainAuthoringDataPath
        );

        Repaint();
    }

    private void DrawDerivedHeightfieldLayout()
    {
        GUILayout.Space(8f);

        GUILayout.Label(
            "Derived Heightfield Layout",
            EditorStyles.boldLabel
        );

        int tileChunkSpan =
            Mathf.Max(
                1,
                inputHeightTileChunkSpan
            );

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

                "The final authoring height tile along one or " +
                "both axes will extend beyond the playable world.",
                MessageType.Info
            );
        }

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "All height sources produce the same tiled RFloat " +
            "authoring heightfield layout.\n\n" +

            "Adjacent tiles share their boundary samples so the " +
            "heightfield remains continuous across tile edges.",
            MessageType.Info
        );
    }

    private void UpdateHeightAuthoringSettings()
    {
        if (
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            return;
        }

        Undo.RecordObjects(
            new Object[]
            {
                worldSettings,
                terrainAuthoringData
            },
            "Update Height Authoring Settings"
        );

        // =================================================
        // HEIGHTFIELD LAYOUT
        // =================================================

        worldSettings.heightTileChunkSpan =
            Mathf.Max(
                1,
                inputHeightTileChunkSpan
            );

        // =================================================
        // PROCEDURAL SETTINGS
        // =================================================

        /*
         * Preserve these values even if the currently selected
         * source is Flat or Imported.
         *
         * That way switching source modes does not lose the
         * previously configured procedural settings.
         */

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

        // =================================================
        // AUTHORING SETTINGS
        // =================================================

        terrainAuthoringData.sourceMode =
            inputHeightSourceMode;

        terrainAuthoringData.flatHeight =
            inputFlatHeight;

        terrainAuthoringData.importedHeightmap =
            inputImportedHeightmap;

        // =================================================
        // SAVE
        // =================================================

        EditorUtility.SetDirty(
            worldSettings
        );

        EditorUtility.SetDirty(
            terrainAuthoringData
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            terrainAuthoringData
        );

        Repaint();

        Debug.Log(
            "Height authoring settings updated.\n\n" +

            $"Source Mode: " +
            $"{terrainAuthoringData.sourceMode}\n" +

            $"Tile Chunk Span: " +
            $"{worldSettings.heightTileChunkSpan}\n" +

            $"Tile World Size: " +
            $"{worldSettings.HeightTileWorldSize}\n" +

            $"Height Tile Grid: " +
            $"{worldSettings.HeightTileGridWidth} x " +
            $"{worldSettings.HeightTileGridHeight}\n" +

            $"Samples Per Tile: " +
            $"{worldSettings.HeightTileSamplesPerSide} x " +
            $"{worldSettings.HeightTileSamplesPerSide}"
        );
    }

    private void InitializeAuthoringHeightfield()
    {
        if (
            worldSettings == null
            ||
            terrainAuthoringData == null
        )
        {
            return;
        }

        // -------------------------------------------------
        // Imported placeholder
        // -------------------------------------------------

        if (
            inputHeightSourceMode ==
            TerrainHeightSourceMode.Imported
        )
        {
            EditorUtility.DisplayDialog(
                "Imported Heightfields",

                "Imported terrain height initialization is " +
                "not implemented yet.",

                "OK"
            );

            return;
        }

        // -------------------------------------------------
        // Confirm destructive reinitialization
        // -------------------------------------------------

        if (
            terrainAuthoringData.authoringRevision > 0
        )
        {
            bool confirmed =
                EditorUtility.DisplayDialog(
                    "Reinitialize Authoring Heightfield",

                    "This will replace the current committed " +
                    "authoring heightfield.\n\n" +

                    "Existing baked authoring height data will " +
                    "be overwritten.\n\n" +

                    "Continue?",

                    "Reinitialize",
                    "Cancel"
                );

            if (!confirmed)
            {
                return;
            }
        }

        // -------------------------------------------------
        // Save staged settings first
        // -------------------------------------------------

        UpdateHeightAuthoringSettings();

        // -------------------------------------------------
        // Initialize
        // -------------------------------------------------

        TerrainAuthoringHeightInitializer
            .InitializeHeightfield(
                worldSettings,
                terrainAuthoringData
            );

        Repaint();
    }

    private void DrawCurrentRuntimeHeightmapPipeline()
    {
        GUILayout.Space(15f);

        GUILayout.Label(
            "Runtime Heightmaps",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Runtime heightmaps are compiled from the committed " +
            "authoring heightfield.\n\n" +

            "Authoring/Height/Tiles is the editable source of " +
            "truth. Generated/Heightmaps/Tiles is derived runtime " +
            "output and can be regenerated by compiling again.",
            MessageType.Info
        );

        TerrainAuthoringHeightManifest authoringManifest =
            TerrainAuthoringStateUtility
                .LoadAuthoringHeightManifest();

        string authoringHeightfieldState;

        if (authoringManifest == null)
        {
            authoringHeightfieldState =
                "Manifest Missing";
        }
        else if (!authoringManifest.isComplete)
        {
            authoringHeightfieldState =
                "Incomplete";
        }
        else if (
            authoringManifest.manifestVersion !=
            TerrainAuthoringHeightManifest.CurrentVersion
        )
        {
            authoringHeightfieldState =
                "Out of Date";
        }
        else if (
            !authoringManifest.HasValidCommittedHeightRange
        )
        {
            authoringHeightfieldState =
                "Invalid Height Range";
        }
        else
        {
            authoringHeightfieldState =
                "Complete";
        }

        EditorGUILayout.LabelField(
            "Authoring Revision",
            terrainAuthoringData
                .authoringRevision
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Authoring Heightfield State",
            authoringHeightfieldState
        );

        EditorGUILayout.LabelField(
            "Committed Height Revision",
            authoringManifest != null
                ? authoringManifest
                    .committedHeightRevision
                    .ToString()
                : "-"
        );

        if (
            authoringManifest != null
            &&
            authoringManifest.HasValidCommittedHeightRange
        )
        {
            EditorGUILayout.LabelField(
                "Committed Height Range",
                $"{authoringManifest.minimumCommittedHeight:R} -> " +
                $"{authoringManifest.maximumCommittedHeight:R}"
            );
        }

        EditorGUILayout.LabelField(
            "Authoring Source Folder",
            WorldMeshesPaths.AuthoringHeightTiles
        );

        EditorGUILayout.LabelField(
            "Runtime Output Folder",
            TerrainRuntimeHeightAssetUtility
                .HeightmapTileFolder
        );

        GUILayout.Space(8f);

        bool authoringReady =
            terrainAuthoringData.authoringRevision > 0
            &&
            authoringManifest != null
            &&
            authoringManifest.isComplete
            &&
            authoringManifest.manifestVersion ==
                TerrainAuthoringHeightManifest.CurrentVersion
            &&
            authoringManifest.HasValidCommittedHeightRange;

        if (!authoringReady)
        {
            EditorGUILayout.HelpBox(
                "The committed authoring heightfield is not " +
                "currently valid for runtime compilation.\n\n" +
                "Initialize/Reinitialize the authoring heightfield " +
                "successfully before compiling.",
                MessageType.Warning
            );
        }

        EditorGUI.BeginDisabledGroup(
            !authoringReady
        );

        if (
            GUILayout.Button(
                "Compile Runtime Heightmaps",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainRuntimeHeightCompiler
                .CompileRuntimeHeightmaps(
                    worldSettings,
                    terrainAuthoringData
                );

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Validate Runtime Heightmap Tiles",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainHeightmapValidator
                .ValidateHeightmaps(
                    worldSettings
                );
        }

        GUILayout.Space(10f);

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

        if (
            heightmapStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.OutOfDate
        )
        {
            EditorGUILayout.HelpBox(
                "The compiled runtime heightmaps are out of date " +
                "with the current committed authoring heightfield " +
                "or heightfield layout. Compile them again before " +
                "preparing runtime streaming data.",
                MessageType.Warning
            );
        }

        // =================================================
        // ADDRESSABLES
        // =================================================

        EditorGUI.BeginDisabledGroup(
            !heightmapsCurrent
        );

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Configure Heightmap Addressables",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainHeightmapAddressablesUtility
                .PrepareHeightmapTilesForRuntime();
        }

        GUILayout.Space(5f);

        if (
            GUILayout.Button(
                "Configure + Build Addressables Content",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            bool confirmed =
                EditorUtility.DisplayDialog(
                    "Build Addressables Content",

                    "This will configure the generated heightmap " +
                    "tiles as Addressables and rebuild the project's " +
                    "Addressables player content.\n\n" +

                    "This is required when testing with " +
                    "'Use Existing Build'.\n\n" +

                    "Continue?",

                    "Build",
                    "Cancel"
                );

            if (confirmed)
            {
                TerrainHeightmapAddressablesUtility
                    .PrepareAndBuildHeightmapTilesForRuntime();
            }
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Use Asset Database (fastest):\n" +
            "A new Addressables build is not required after every " +
            "terrain compilation.\n\n" +

            "Use Existing Build:\n" +
            "Run Configure + Build Addressables Content after " +
            "compiling new runtime heightmaps.",
            MessageType.Info
        );
    }
}
