using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    // =====================================================
    // CLIPMAP INPUTS
    // =====================================================

    [SerializeField]
    private int inputClipmapCenterResolution =
        64;

    [SerializeField]
    private int inputClipmapLevelCount =
        6;

    [SerializeField]
    private int inputClipmapBaseSampleStep =
        1;

    /*
     * User-facing staged coverage requests for LOD1+.
     *
     * These floats exist only in the editor UI. WorldSettings
     * stores exact integer outer resolutions as the authoritative
     * generated topology.
     *
     * Index 0 = LOD1.
     */
    [SerializeField]
    private float[] inputClipmapLODOuterCoverages =
        new float[
            TerrainClipmapTopologyUtility.MaximumLevelCount -
            1
        ];

    private WorldSettings clipmapInputSource;

    // =====================================================
    // LOAD
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
                TerrainClipmapTopologyUtility.MinimumOuterResolution,
                worldSettings.clipmapCenterResolution
            );

        inputClipmapLevelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                TerrainClipmapTopologyUtility.MinimumLevelCount,
                TerrainClipmapTopologyUtility.MaximumLevelCount
            );

        inputClipmapBaseSampleStep =
            Mathf.Max(
                1,
                worldSettings.clipmapBaseSampleStep
            );

        EnsureClipmapLODCoverageInputCapacity();

        for (
            int level = 1;
            level <
                TerrainClipmapTopologyUtility.MaximumLevelCount;
            level++
        )
        {
            inputClipmapLODOuterCoverages[
                level - 1
            ] =
                TerrainClipmapTopologyUtility
                    .GetLODOuterCoverage(
                        worldSettings,
                        level
                    );
        }

        clipmapInputSource =
            worldSettings;

        Repaint();
    }

    // =====================================================
    // UPDATE
    // =====================================================

    private void UpdateClipmapSettings()
    {
        if (worldSettings == null)
        {
            return;
        }

        int centerResolution =
            Mathf.Max(
                TerrainClipmapTopologyUtility.MinimumOuterResolution,
                inputClipmapCenterResolution
            );

        int levelCount =
            Mathf.Clamp(
                inputClipmapLevelCount,
                TerrainClipmapTopologyUtility.MinimumLevelCount,
                TerrainClipmapTopologyUtility.MaximumLevelCount
            );

        int baseSampleStep =
            Mathf.Max(
                1,
                inputClipmapBaseSampleStep
            );

        // -------------------------------------------------
        // Validate LOD0 resolution
        // -------------------------------------------------

        if (
            centerResolution %
                TerrainClipmapTopologyUtility
                    .OuterResolutionAlignment
            !=
            0
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

        inputClipmapCenterResolution =
            centerResolution;

        inputClipmapLevelCount =
            levelCount;

        inputClipmapBaseSampleStep =
            baseSampleStep;

        EnsureClipmapLODCoverageInputCapacity();

        float stagedBaseSpacing =
            CalculateStagedClipmapBaseSpacing();

        int[] savedOuterResolutions =
            ResolveStagedLODOuterResolutions(
                stagedBaseSpacing,
                levelCount
            );

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

        worldSettings.clipmapLODOuterResolutions =
            savedOuterResolutions;

        EditorUtility.SetDirty(
            worldSettings
        );

        AssetDatabase.SaveAssetIfDirty(
            worldSettings
        );

        /*
         * Reload the staged world-space coverage from the exact
         * saved topology so aligned values become the new visible
         * baseline without preserving a second source of truth.
         */
        LoadClipmapSettingsIntoEditor();

        float outerDiameter =
            TerrainClipmapTopologyUtility
                .CalculateClipmapDiameter(
                    worldSettings
                );

        long estimatedTriangles =
            TerrainClipmapTopologyUtility
                .CalculateTotalTriangleCount(
                    worldSettings
                );

        Debug.Log(
            "Clipmap settings updated.\n\n" +

            $"Center Resolution: " +
            $"{centerResolution}\n" +

            $"LOD Levels: " +
            $"{levelCount}\n" +

            $"Base Sample Step: " +
            $"{baseSampleStep}\n" +

            $"Base Vertex Spacing: " +
            $"{worldSettings.ClipmapBaseSpacing}\n\n" +

            $"Outer Coverage: " +
            $"{outerDiameter} x " +
            $"{outerDiameter}\n" +

            $"Estimated Triangles: " +
            $"{estimatedTriangles:N0}\n\n" +

            "Regenerate Clipmap Meshes before running " +
            "Setup / Repair World Hierarchy."
        );
    }

    // =====================================================
    // DRAW
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

        EnsureClipmapLODCoverageInputCapacity();

        // -------------------------------------------------
        // LOD0 inputs
        // -------------------------------------------------

        float oldLabelWidth =
            EditorGUIUtility.labelWidth;

        EditorGUIUtility.labelWidth =
            165f;

        GUILayout.Label(
            "LOD0",
            EditorStyles.boldLabel
        );

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

        // -------------------------------------------------
        // Clamp basic ranges
        // -------------------------------------------------

        inputClipmapCenterResolution =
            Mathf.Max(
                TerrainClipmapTopologyUtility.MinimumOuterResolution,
                inputClipmapCenterResolution
            );

        inputClipmapLevelCount =
            Mathf.Clamp(
                inputClipmapLevelCount,
                TerrainClipmapTopologyUtility.MinimumLevelCount,
                TerrainClipmapTopologyUtility.MaximumLevelCount
            );

        inputClipmapBaseSampleStep =
            Mathf.Max(
                1,
                inputClipmapBaseSampleStep
            );

        bool centerResolutionValid =
            inputClipmapCenterResolution %
                TerrainClipmapTopologyUtility
                    .OuterResolutionAlignment
            ==
            0;

        if (!centerResolutionValid)
        {
            GUILayout.Space(5f);

            EditorGUILayout.HelpBox(
                "Center Resolution must be evenly divisible " +
                "by 4 so adjacent 2:1 LOD boundaries can be " +
                "stitched exactly.",
                MessageType.Warning
            );
        }

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
            CalculateStagedClipmapBaseSpacing();

        float lod0Coverage =
            inputClipmapCenterResolution *
            stagedBaseSpacing;

        EditorGUILayout.LabelField(
            "Vertex Spacing",
            stagedBaseSpacing.ToString()
        );

        EditorGUILayout.LabelField(
            "Outer Coverage",
            $"{lod0Coverage} x {lod0Coverage}"
        );

        // -------------------------------------------------
        // Per-LOD coverage
        // -------------------------------------------------

        if (
            inputClipmapLevelCount >
            1
        )
        {
            GUILayout.Space(10f);

            GUILayout.Label(
                "LOD Ring Coverage",
                EditorStyles.boldLabel
            );

            EditorGUILayout.HelpBox(
                "Each coarser LOD keeps the existing 2:1 " +
                "vertex-spacing relationship. Outer Coverage " +
                "controls how far that LOD extends.\n\n" +
                "Coverage is rounded upward to the nearest " +
                "grid-aligned resolution divisible by 4. Inner " +
                "ring boundaries remain derived automatically " +
                "from the previous finer LOD.",
                MessageType.Info
            );

            int finerOuterResolution =
                inputClipmapCenterResolution;

            for (
                int level = 1;
                level < inputClipmapLevelCount;
                level++
            )
            {
                int index =
                    level -
                    1;

                float spacing =
                    stagedBaseSpacing *
                    Mathf.Pow(
                        2f,
                        level
                    );

                GUILayout.Space(4f);

                GUILayout.Label(
                    $"LOD{level}",
                    EditorStyles.boldLabel
                );

                float requestedCoverage =
                    Mathf.Max(
                        0f,
                        inputClipmapLODOuterCoverages[
                            index
                        ]
                    );

                requestedCoverage =
                    EditorGUILayout.FloatField(
                        "Outer Coverage",
                        requestedCoverage
                    );

                inputClipmapLODOuterCoverages[
                    index
                ] =
                    Mathf.Max(
                        0f,
                        requestedCoverage
                    );

                int resolvedResolution =
                    TerrainClipmapTopologyUtility
                        .ResolveOuterResolutionForCoverage(
                            requestedCoverage,
                            spacing,
                            finerOuterResolution
                        );

                float resolvedCoverage =
                    resolvedResolution *
                    spacing;

                GUILayout.Space(3f);

                EditorGUILayout.LabelField(
                    $"LOD{level} Generated Coverage",
                    $"{resolvedCoverage} x " +
                    $"{resolvedCoverage}"
                );

                EditorGUILayout.LabelField(
                    $"LOD{level} Vertex Spacing",
                    spacing.ToString()
                );

                EditorGUILayout.LabelField(
                    $"LOD{level} Outer Resolution",
                    resolvedResolution.ToString()
                );

                if (
                    !Mathf.Approximately(
                        requestedCoverage,
                        resolvedCoverage
                    )
                )
                {
                    EditorGUILayout.HelpBox(
                        $"LOD{level} will align upward from " +
                        $"{requestedCoverage} to " +
                        $"{resolvedCoverage} world units.",
                        MessageType.None
                    );
                }

                finerOuterResolution =
                    resolvedResolution;

                GUILayout.Space(5f);
            }
        }

        EditorGUIUtility.labelWidth =
            oldLabelWidth;

        // -------------------------------------------------
        // Resolve staged topology
        // -------------------------------------------------

        int[] stagedOuterResolutions =
            ResolveStagedLODOuterResolutions(
                stagedBaseSpacing,
                inputClipmapLevelCount
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

        int outermostResolution =
            inputClipmapLevelCount <= 1
                ? inputClipmapCenterResolution
                : stagedOuterResolutions[
                    inputClipmapLevelCount -
                    2
                ];

        float outermostSpacing =
            stagedBaseSpacing *
            Mathf.Pow(
                2f,
                inputClipmapLevelCount - 1
            );

        float stagedOuterDiameter =
            outermostResolution *
            outermostSpacing;

        // -------------------------------------------------
        // Derived summary
        // -------------------------------------------------

        GUILayout.Space(5f);

        EditorGUILayout.LabelField(
            "Height Sample Spacing",
            heightSampleSpacing.ToString()
        );

        EditorGUILayout.LabelField(
            "Generated Assets",
            generatedAssetCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Final Outer Coverage",
            $"{stagedOuterDiameter} x " +
            $"{stagedOuterDiameter}"
        );

        // -------------------------------------------------
        // Triangle estimate
        // -------------------------------------------------

        GUILayout.Space(5f);

        long totalTriangles =
            TerrainClipmapTopologyUtility
                .CalculateCenterTriangleCount(
                    inputClipmapCenterResolution
                );

        string triangleSummary =
            $"LOD0: {totalTriangles:N0}";

        int triangleFinerOuterResolution =
            inputClipmapCenterResolution;

        for (
            int level = 1;
            level < inputClipmapLevelCount;
            level++
        )
        {
            int outerResolution =
                stagedOuterResolutions[
                    level - 1
                ];

            long ringTriangles =
                TerrainClipmapTopologyUtility
                    .CalculateRingTriangleCount(
                        outerResolution,
                        triangleFinerOuterResolution
                    );

            long stitchTriangles =
                TerrainClipmapTopologyUtility
                    .CalculateStitchTriangleCount(
                        triangleFinerOuterResolution
                    );

            long levelTriangles =
                ringTriangles +
                stitchTriangles;

            totalTriangles +=
                levelTriangles;

            triangleSummary +=
                $"\nLOD{level}: " +
                $"{levelTriangles:N0} " +
                $"({ringTriangles:N0} ring + " +
                $"{stitchTriangles:N0} stitch)";

            triangleFinerOuterResolution =
                outerResolution;
        }

        EditorGUILayout.HelpBox(
            "Estimated Triangles\n\n" +
            triangleSummary +
            "\n\n" +
            $"Total: {totalTriangles:N0}",
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
            "is a hollow ring with independently configurable " +
            "outer coverage and a dedicated 2:1 stitch mesh " +
            "joining it to the previous level.\n\n" +

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
            "if you have changed the fields above. After a " +
            "topology change, regenerate the clipmap meshes " +
            "before running Setup / Repair World Hierarchy.",
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

            "The center, variable-sized rings, and stitch meshes " +
            "are treated as one combined surface and checked for " +
            "cracks, holes, overlapping triangles, non-manifold " +
            "edges, degenerate triangles, incorrect winding, and " +
            "incorrect per-LOD/overall coverage.\n\n" +

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
    // STAGED LOD HELPERS
    // =====================================================

    private void EnsureClipmapLODCoverageInputCapacity()
    {
        int requiredLength =
            TerrainClipmapTopologyUtility.MaximumLevelCount -
            1;

        if (
            inputClipmapLODOuterCoverages != null
            &&
            inputClipmapLODOuterCoverages.Length ==
                requiredLength
        )
        {
            return;
        }

        float[] replacement =
            new float[
                requiredLength
            ];

        if (
            inputClipmapLODOuterCoverages != null
        )
        {
            int copyCount =
                Mathf.Min(
                    replacement.Length,
                    inputClipmapLODOuterCoverages.Length
                );

            for (
                int index = 0;
                index < copyCount;
                index++
            )
            {
                replacement[index] =
                    inputClipmapLODOuterCoverages[
                        index
                    ];
            }
        }

        inputClipmapLODOuterCoverages =
            replacement;
    }

    private float CalculateStagedClipmapBaseSpacing()
    {
        if (worldSettings == null)
        {
            return
                1f;
        }

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

        return
            heightSampleSpacing *
            Mathf.Max(
                1,
                inputClipmapBaseSampleStep
            );
    }

    private int[] ResolveStagedLODOuterResolutions(
        float baseSpacing,
        int levelCount
    )
    {
        EnsureClipmapLODCoverageInputCapacity();

        int safeLevelCount =
            Mathf.Clamp(
                levelCount,
                TerrainClipmapTopologyUtility.MinimumLevelCount,
                TerrainClipmapTopologyUtility.MaximumLevelCount
            );

        int[] resolved =
            new int[
                Mathf.Max(
                    0,
                    safeLevelCount - 1
                )
            ];

        int finerResolution =
            inputClipmapCenterResolution;

        for (
            int level = 1;
            level < safeLevelCount;
            level++
        )
        {
            int index =
                level -
                1;

            float spacing =
                Mathf.Max(
                    0.0001f,
                    baseSpacing
                )
                *
                Mathf.Pow(
                    2f,
                    level
                );

            float requestedCoverage =
                inputClipmapLODOuterCoverages[
                    index
                ];

            /*
             * Newly exposed levels may not yet have a staged
             * coverage value. Preserve legacy behavior by starting
             * them at the physical coverage produced by the LOD0
             * resolution at this level's spacing.
             */
            if (
                requestedCoverage <=
                    0f
            )
            {
                requestedCoverage =
                    inputClipmapCenterResolution *
                    spacing;

                inputClipmapLODOuterCoverages[
                    index
                ] =
                    requestedCoverage;
            }

            int resolution =
                TerrainClipmapTopologyUtility
                    .ResolveOuterResolutionForCoverage(
                        requestedCoverage,
                        spacing,
                        finerResolution
                    );

            resolved[index] =
                resolution;

            finerResolution =
                resolution;
        }

        return
            resolved;
    }
}
