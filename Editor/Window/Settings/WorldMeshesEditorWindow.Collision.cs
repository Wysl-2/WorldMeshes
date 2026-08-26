using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    // =====================================================
    // COLLISION INPUTS
    // =====================================================

    [SerializeField]
    private int inputCollisionResolution = 64;

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
        // ASSET SEAM VALIDATION
        // =====================================================

        GUILayout.Space(10f);

        EditorGUILayout.HelpBox(
            "Validates every shared generated collision-mesh edge.\n\n" +

            "Corresponding boundary vertices are compared " +
            "between neighboring chunk Mesh assets before runtime " +
            "streaming is involved.\n\n" +

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
        // RUNTIME PHYSICS VALIDATION
        // =====================================================

        GUILayout.Space(10f);

        GUILayout.Label(
            "Runtime Physics Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Validates the collision system that is actually active " +
            "in Play Mode.\n\n" +

            "The validator checks the resident Addressables window, " +
            "the fixed MeshCollider pool, per-chunk Mesh assignments, " +
            "real PhysX raycasts, and every seam between neighboring " +
            "active collider chunks.\n\n" +

            "Run it only after collision streaming has finished " +
            "synchronizing at the current Player chunk.",
            MessageType.Info
        );

        if (!EditorApplication.isPlaying)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                "Enter Play Mode to validate the streamed runtime " +
                "collision system.",
                MessageType.None
            );
        }

        EditorGUI.BeginDisabledGroup(
            !EditorApplication.isPlaying
        );

        if (
            GUILayout.Button(
                "Validate Runtime Collision Physics",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainCollisionPhysicsValidator
                .ValidateRuntimeCollisionPhysics(
                    worldSettings
                );
        }

        EditorGUI.EndDisabledGroup();

        // =====================================================
        // RUNTIME STREAMING PREPARATION
        // =====================================================

        GUILayout.Space(10f);

        GUILayout.Label(
            "Runtime Streaming",
            EditorStyles.boldLabel
        );

        TerrainGenerationStateUtility.GenerationStatus
            collisionStatus =
                TerrainGenerationStateUtility
                    .GetCollisionMeshStatus(
                        worldSettings
                    );

        bool collisionMeshesCurrent =
            collisionStatus ==
            TerrainGenerationStateUtility
                .GenerationStatus.Current;

        TerrainCollisionManifest collisionManifest =
            AssetDatabase
                .LoadAssetAtPath<TerrainCollisionManifest>(
                    WorldMeshesPaths
                        .CollisionManifestAssetPath
                );

        bool manifestMatchesCurrentGeneration =
            collisionManifest != null
            &&
            collisionManifest.isComplete
            &&
            collisionManifest.gridWidth ==
                Mathf.Max(
                    1,
                    worldSettings.gridWidth
                )
            &&
            collisionManifest.gridHeight ==
                Mathf.Max(
                    1,
                    worldSettings.gridHeight
                )
            &&
            collisionManifest.collisionMeshGenerationRevision ==
                worldSettings
                    .collisionMeshGenerationRevision
            &&
            collisionManifest.collisionSourceHeightmapGenerationRevision ==
                worldSettings
                    .collisionSourceHeightmapGenerationRevision;

        EditorGUILayout.LabelField(
            "Collision State",
            TerrainGenerationStateUtility
                .GetStatusLabel(
                    collisionStatus
                )
        );

        EditorGUILayout.LabelField(
            "Runtime Manifest",
            collisionManifest == null
                ? "Not Prepared"
                : collisionManifest.isComplete
                    ? manifestMatchesCurrentGeneration
                        ? "Current"
                        : "Out Of Date"
                    : "Incomplete"
        );

        int regionSpan =
            TerrainCollisionAddressablesUtility
                .CollisionRegionChunkSpan;

        int regionGridWidth =
            Mathf.CeilToInt(
                (float)Mathf.Max(
                    1,
                    worldSettings.gridWidth
                )
                /
                regionSpan
            );

        int regionGridHeight =
            Mathf.CeilToInt(
                (float)Mathf.Max(
                    1,
                    worldSettings.gridHeight
                )
                /
                regionSpan
            );

        EditorGUILayout.LabelField(
            "Runtime Meshes",
            (
                Mathf.Max(
                    1,
                    worldSettings.gridWidth
                )
                *
                Mathf.Max(
                    1,
                    worldSettings.gridHeight
                )
            ).ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Region Chunk Span",
            $"{regionSpan} x {regionSpan}"
        );

        EditorGUILayout.LabelField(
            "Region Grid",
            $"{regionGridWidth} x {regionGridHeight}"
        );

        EditorGUILayout.LabelField(
            "Expected Regions",
            (regionGridWidth * regionGridHeight)
                .ToString("N0")
        );

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            "Preparing collision meshes for runtime registers " +
            "the existing generated Mesh assets with Unity " +
            "Addressables.\n\n" +

            "Each collision chunk keeps its own deterministic " +
            "runtime address. Chunks are additionally grouped into " +
            "8 x 8 packaging regions so the project does not create " +
            "one Addressables bundle per collision mesh.\n\n" +

            "No collision mesh geometry is regenerated or modified.",
            MessageType.Info
        );

        if (!collisionMeshesCurrent)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                "The generated collision meshes are not current.\n\n" +

                "Generate or regenerate collision meshes before " +
                "preparing them for runtime streaming.",
                MessageType.Warning
            );
        }

        EditorGUI.BeginDisabledGroup(
            !collisionMeshesCurrent
        );

        if (
            GUILayout.Button(
                "Prepare Collision Meshes For Runtime",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainCollisionAddressablesUtility
                .PrepareCollisionMeshesForRuntime(
                    worldSettings
                );

            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Addressables Group",
            TerrainCollisionAddressablesUtility
                .CollisionAddressablesGroupName
        );

        EditorGUILayout.LabelField(
            "Address Pattern",
            TerrainCollisionManifest
                .CollisionMeshAddressPrefix
            +
            "_X_Z"
        );

        EditorGUILayout.LabelField(
            "Manifest Path",
            WorldMeshesPaths
                .CollisionManifestAssetPath
        );

        GUILayout.EndVertical();
    }

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
}
