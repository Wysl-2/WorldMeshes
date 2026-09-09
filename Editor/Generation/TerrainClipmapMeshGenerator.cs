using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainClipmapMeshGenerator
{
    // =====================================================
    // PATHS
    // =====================================================

    public const string ClipmapMeshFolder =
        WorldMeshesPaths.GeneratedClipmapMeshes;

    // =====================================================
    // GENERATE CLIPMAP MESHES
    // =====================================================

    public static void GenerateClipmapMeshes(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Validate
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot generate clipmap meshes: " +
                "WorldSettings is null."
            );

            return;
        }

        if (
            EditorApplication
                .isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Clipmap mesh generation must be " +
                "performed outside Play Mode."
            );

            return;
        }

        // -------------------------------------------------
        // Settings
        // -------------------------------------------------

        int centerResolution =
            Mathf.Max(
                8,
                worldSettings.clipmapCenterResolution
            );

        int levelCount =
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                1,
                10
            );

        int baseSampleStep =
            Mathf.Max(
                1,
                worldSettings.clipmapBaseSampleStep
            );

        float baseSpacing =
            worldSettings.ClipmapBaseSpacing;

        // -------------------------------------------------
        // Validate topology
        // -------------------------------------------------

        if (
            !TerrainClipmapTopologyUtility
                .TryValidateSettings(
                    worldSettings,
                    out string topologyError
                )
        )
        {
            Debug.LogError(
                "Cannot generate clipmap meshes.\n\n" +
                topologyError
            );

            return;
        }

        // -------------------------------------------------
        // Ensure output folder
        // -------------------------------------------------

        EnsureFoldersExist();

        // -------------------------------------------------
        // Expected assets
        // -------------------------------------------------

        HashSet<string> expectedAssetPaths =
            new HashSet<string>();

        int totalAssets =
            1 +
            Mathf.Max(
                0,
                levelCount - 1
            )
            *
            2;

        int currentAsset =
            0;

        int createdCount =
            0;

        int updatedCount =
            0;

        int removedCount =
            0;

        bool cancelled =
            false;

        try
        {
            // =================================================
            // CENTER
            // =================================================

            cancelled =
                ShowProgress(
                    "Generating clipmap center",

                    "LOD0",

                    currentAsset,
                    totalAssets
                );

            if (cancelled)
            {
                return;
            }

            string centerPath =
                GetCenterMeshPath();

            expectedAssetPaths.Add(
                centerPath
            );

            GeneratedMeshData centerData =
                BuildCenterMesh(
                    centerResolution,
                    baseSpacing
                );

            SaveResult centerSaveResult =
                SaveOrUpdateMesh(
                    centerPath,
                    GetCenterMeshName(),
                    centerData
                );

            if (
                centerSaveResult ==
                SaveResult.Created
            )
            {
                createdCount++;
            }
            else
            {
                updatedCount++;
            }

            currentAsset++;

            // =================================================
            // OUTER LEVELS
            // =================================================

            for (
                int level = 1;
                level < levelCount;
                level++
            )
            {
                if (cancelled)
                {
                    break;
                }

                // ---------------------------------------------
                // Ring
                // ---------------------------------------------

                cancelled =
                    ShowProgress(
                        "Generating clipmap ring",

                        $"LOD{level}",

                        currentAsset,
                        totalAssets
                    );

                if (cancelled)
                {
                    break;
                }

                float levelSpacing =
                    TerrainClipmapTopologyUtility
                        .GetLODSpacing(
                            worldSettings,
                            level
                        );

                int outerResolution =
                    TerrainClipmapTopologyUtility
                        .GetLODOuterResolution(
                            worldSettings,
                            level
                        );

                int finerOuterResolution =
                    TerrainClipmapTopologyUtility
                        .GetLODOuterResolution(
                            worldSettings,
                            level - 1
                        );

                string ringPath =
                    GetRingMeshPath(
                        level
                    );

                expectedAssetPaths.Add(
                    ringPath
                );

                GeneratedMeshData ringData =
                    BuildRingMesh(
                        outerResolution,
                        finerOuterResolution,
                        levelSpacing
                    );

                SaveResult ringSaveResult =
                    SaveOrUpdateMesh(
                        ringPath,
                        GetRingMeshName(
                            level
                        ),
                        ringData
                    );

                if (
                    ringSaveResult ==
                    SaveResult.Created
                )
                {
                    createdCount++;
                }
                else
                {
                    updatedCount++;
                }

                currentAsset++;

                // ---------------------------------------------
                // Stitch
                // ---------------------------------------------

                cancelled =
                    ShowProgress(
                        "Generating clipmap transition",

                        $"LOD{level - 1} -> LOD{level}",

                        currentAsset,
                        totalAssets
                    );

                if (cancelled)
                {
                    break;
                }

                float fineSpacing =
                    TerrainClipmapTopologyUtility
                        .GetLODSpacing(
                            worldSettings,
                            level - 1
                        );

                string stitchPath =
                    GetStitchMeshPath(
                        level - 1,
                        level
                    );

                expectedAssetPaths.Add(
                    stitchPath
                );

                GeneratedMeshData stitchData =
                    BuildStitchMesh(
                        finerOuterResolution,
                        fineSpacing
                    );

                SaveResult stitchSaveResult =
                    SaveOrUpdateMesh(
                        stitchPath,
                        GetStitchMeshName(
                            level - 1,
                            level
                        ),
                        stitchData
                    );

                if (
                    stitchSaveResult ==
                    SaveResult.Created
                )
                {
                    createdCount++;
                }
                else
                {
                    updatedCount++;
                }

                currentAsset++;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Clipmap mesh generation failed.\n\n" +
                exception
            );

            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // -------------------------------------------------
        // Cancelled
        // -------------------------------------------------

        if (cancelled)
        {
            AssetDatabase.SaveAssets();

            Debug.LogWarning(
                "Clipmap mesh generation cancelled.\n\n" +

                "Meshes generated before cancellation " +
                "were preserved."
            );

            return;
        }

        // -------------------------------------------------
        // Remove obsolete generated clipmap meshes
        // -------------------------------------------------

        removedCount =
            RemoveObsoleteMeshes(
                expectedAssetPaths
            );

        // -------------------------------------------------
        // Save
        // -------------------------------------------------

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // -------------------------------------------------
        // Select center
        // -------------------------------------------------

        Mesh centerMesh =
            AssetDatabase.LoadAssetAtPath<Mesh>(
                GetCenterMeshPath()
            );

        if (centerMesh != null)
        {
            Selection.activeObject =
                centerMesh;
        }

        // -------------------------------------------------
        // Derived coverage
        // -------------------------------------------------

        float outerDiameter =
            TerrainClipmapTopologyUtility
                .CalculateClipmapDiameter(
                    worldSettings
                );

        // -------------------------------------------------
        // Complete
        // -------------------------------------------------

        Debug.Log(
            "Clipmap mesh generation complete.\n\n" +

            $"Center Resolution: " +
            $"{centerResolution}\n" +

            $"LOD Levels: " +
            $"{levelCount}\n" +

            $"Base Sample Step: " +
            $"{baseSampleStep}\n" +

            $"Base Vertex Spacing: " +
            $"{baseSpacing}\n\n" +

            $"Outer Coverage: " +
            $"{outerDiameter} x " +
            $"{outerDiameter}\n\n" +

            $"Generated Assets: " +
            $"{totalAssets}\n" +

            $"Created: " +
            $"{createdCount}\n" +

            $"Updated: " +
            $"{updatedCount}\n" +

            $"Removed Obsolete: " +
            $"{removedCount}\n\n" +

            $"Output Folder:\n" +
            $"{ClipmapMeshFolder}"
        );
    }

    // =====================================================
    // BUILD CENTER
    // =====================================================

    private static GeneratedMeshData BuildCenterMesh(
        int resolution,
        float spacing
    )
    {
        MeshBuilder builder =
            new MeshBuilder(
                spacing
            );

        int halfResolution =
            resolution /
            2;

        for (
            int z = -halfResolution;
            z < halfResolution;
            z++
        )
        {
            for (
                int x = -halfResolution;
                x < halfResolution;
                x++
            )
            {
                builder.AddGridQuad(
                    x,
                    z
                );
            }
        }

        return
            builder.Build();
    }

    // =====================================================
    // BUILD RING
    // =====================================================

    private static GeneratedMeshData BuildRingMesh(
        int outerResolution,
        int finerOuterResolution,
        float spacing
    )
    {
        MeshBuilder builder =
            new MeshBuilder(
                spacing
            );

        /*
         * Express the ring entirely in units of this LOD's
         * own vertex spacing.
         *
         * The outer boundary is independently configurable.
         * The inner boundary remains derived from the previous
         * finer LOD so the existing 2:1 stitch topology stays
         * authoritative.
         */

        int outerHalf =
            outerResolution /
            2;

        int innerHalf =
            finerOuterResolution /
            4 +
            1;

        for (
            int z = -outerHalf;
            z < outerHalf;
            z++
        )
        {
            for (
                int x = -outerHalf;
                x < outerHalf;
                x++
            )
            {
                bool insideHole =
                    x >= -innerHalf
                    &&
                    x < innerHalf
                    &&
                    z >= -innerHalf
                    &&
                    z < innerHalf;

                if (insideHole)
                {
                    continue;
                }

                builder.AddGridQuad(
                    x,
                    z
                );
            }
        }

        return
            builder.Build();
    }

    // =====================================================
    // BUILD STITCH
    // =====================================================

    private static GeneratedMeshData BuildStitchMesh(
        int fineOuterResolution,
        float fineSpacing
    )
    {
        /*
         * All coordinates here are measured in units of
         * the FINER level's spacing.
         *
         * The next coarse level has exactly twice this
         * spacing.
         */

        int half =
            fineOuterResolution /
            2;

        /*
         * Stage 3B:
         *
         * transitionFineBoundaryHalf tells MeshBuilder which
         * Chebyshev-radius boundary belongs to the finer LOD.
         *
         * Vertices on this boundary receive clipmapData.x = 1.
         * All other stitch vertices receive clipmapData.x = 0.
         */
        MeshBuilder builder =
            new MeshBuilder(
                fineSpacing,
                half
            );

        const int coarseStep =
            2;

        int coarseSegmentsPerSide =
            fineOuterResolution /
            2;

        // =================================================
        // NORTH
        // =================================================

        for (
            int segment = 0;
            segment < coarseSegmentsPerSide;
            segment++
        )
        {
            int t0 =
                -half +
                segment *
                coarseStep;

            int t1 =
                t0 +
                coarseStep;

            int middle =
                t0 +
                1;

            AddTransitionSegment(
                builder,

                new Vector2Int(
                    t0,
                    half
                ),

                new Vector2Int(
                    middle,
                    half
                ),

                new Vector2Int(
                    t1,
                    half
                ),

                new Vector2Int(
                    t0,
                    half + coarseStep
                ),

                new Vector2Int(
                    t1,
                    half + coarseStep
                )
            );
        }

        // =================================================
        // SOUTH
        // =================================================

        for (
            int segment = 0;
            segment < coarseSegmentsPerSide;
            segment++
        )
        {
            int t0 =
                -half +
                segment *
                coarseStep;

            int t1 =
                t0 +
                coarseStep;

            int middle =
                t0 +
                1;

            AddTransitionSegment(
                builder,

                new Vector2Int(
                    t0,
                    -half
                ),

                new Vector2Int(
                    middle,
                    -half
                ),

                new Vector2Int(
                    t1,
                    -half
                ),

                new Vector2Int(
                    t0,
                    -half - coarseStep
                ),

                new Vector2Int(
                    t1,
                    -half - coarseStep
                )
            );
        }

        // =================================================
        // EAST
        // =================================================

        for (
            int segment = 0;
            segment < coarseSegmentsPerSide;
            segment++
        )
        {
            int t0 =
                -half +
                segment *
                coarseStep;

            int t1 =
                t0 +
                coarseStep;

            int middle =
                t0 +
                1;

            AddTransitionSegment(
                builder,

                new Vector2Int(
                    half,
                    t0
                ),

                new Vector2Int(
                    half,
                    middle
                ),

                new Vector2Int(
                    half,
                    t1
                ),

                new Vector2Int(
                    half + coarseStep,
                    t0
                ),

                new Vector2Int(
                    half + coarseStep,
                    t1
                )
            );
        }

        // =================================================
        // WEST
        // =================================================

        for (
            int segment = 0;
            segment < coarseSegmentsPerSide;
            segment++
        )
        {
            int t0 =
                -half +
                segment *
                coarseStep;

            int t1 =
                t0 +
                coarseStep;

            int middle =
                t0 +
                1;

            AddTransitionSegment(
                builder,

                new Vector2Int(
                    -half,
                    t0
                ),

                new Vector2Int(
                    -half,
                    middle
                ),

                new Vector2Int(
                    -half,
                    t1
                ),

                new Vector2Int(
                    -half - coarseStep,
                    t0
                ),

                new Vector2Int(
                    -half - coarseStep,
                    t1
                )
            );
        }

        // =================================================
        // CORNERS
        // =================================================

        /*
         * The four side strips leave one coarse-sized square
         * at each corner.
         *
         * IMPORTANT FOR MOVING LODS:
         *
         * The corner is triangulated along the diagonal from
         * the adaptive FINE corner to the opposite COARSE outer
         * corner.
         *
         * The previous stationary-only diagonal could become
         * degenerate when the fine LOD was offset diagonally by
         * one fine sample in both X and Z.
         *
         * This diagonal remains non-degenerate for every
         * Stage 3A adjacent-LOD offset:
         *
         *     (-S, -S) ... (+S, +S)
         *
         * where S is the finer LOD spacing.
         */

        // North-East
        AddAdaptiveCorner(
            builder,

            new Vector2Int(
                half,
                half
            ),

            new Vector2Int(
                half + coarseStep,
                half
            ),

            new Vector2Int(
                half + coarseStep,
                half + coarseStep
            ),

            new Vector2Int(
                half,
                half + coarseStep
            )
        );

        // North-West
        AddAdaptiveCorner(
            builder,

            new Vector2Int(
                -half,
                half
            ),

            new Vector2Int(
                -half - coarseStep,
                half
            ),

            new Vector2Int(
                -half - coarseStep,
                half + coarseStep
            ),

            new Vector2Int(
                -half,
                half + coarseStep
            )
        );

        // South-East
        AddAdaptiveCorner(
            builder,

            new Vector2Int(
                half,
                -half
            ),

            new Vector2Int(
                half + coarseStep,
                -half
            ),

            new Vector2Int(
                half + coarseStep,
                -half - coarseStep
            ),

            new Vector2Int(
                half,
                -half - coarseStep
            )
        );

        // South-West
        AddAdaptiveCorner(
            builder,

            new Vector2Int(
                -half,
                -half
            ),

            new Vector2Int(
                -half - coarseStep,
                -half
            ),

            new Vector2Int(
                -half - coarseStep,
                -half - coarseStep
            ),

            new Vector2Int(
                -half,
                -half - coarseStep
            )
        );

        return
            builder.Build();
    }

    // =====================================================
    // ADD 2:1 TRANSITION SEGMENT
    // =====================================================

    private static void AddTransitionSegment(
        MeshBuilder builder,

        Vector2Int fine0,
        Vector2Int fineMiddle,
        Vector2Int fine1,

        Vector2Int coarse0,
        Vector2Int coarse1
    )
    {
        /*
         * Fine edge:
         *
         * F0 ---- FM ---- F1
         *
         * Coarse edge:
         *
         * C0 ------------ C1
         *
         * The resulting strip uses three triangles:
         *
         * F0-C0-FM
         * FM-C0-C1
         * FM-C1-F1
         */

        int f0 =
            builder.GetVertex(
                fine0
            );

        int fm =
            builder.GetVertex(
                fineMiddle
            );

        int f1 =
            builder.GetVertex(
                fine1
            );

        int c0 =
            builder.GetVertex(
                coarse0
            );

        int c1 =
            builder.GetVertex(
                coarse1
            );

        builder.AddTriangleUpward(
            f0,
            c0,
            fm
        );

        builder.AddTriangleUpward(
            fm,
            c0,
            c1
        );

        builder.AddTriangleUpward(
            fm,
            c1,
            f1
        );
    }

    // =====================================================
    // ADD ADAPTIVE CORNER
    // =====================================================

    private static void AddAdaptiveCorner(
        MeshBuilder builder,
        Vector2Int fineCorner,
        Vector2Int coarseAlongX,
        Vector2Int coarseOuterCorner,
        Vector2Int coarseAlongZ
    )
    {
        /*
         * Use the fine corner -> outer coarse corner diagonal.
         *
         * Only fineCorner has transition weight 1.
         * The other three vertices remain attached to the
         * coarse ring.
         *
         * This topology stays non-degenerate for every valid
         * adjacent-LOD relative offset calculated in Stage 3A.
         */

        int fine =
            builder.GetVertex(
                fineCorner
            );

        int coarseX =
            builder.GetVertex(
                coarseAlongX
            );

        int coarseOuter =
            builder.GetVertex(
                coarseOuterCorner
            );

        int coarseZ =
            builder.GetVertex(
                coarseAlongZ
            );

        builder.AddTriangleUpward(
            fine,
            coarseX,
            coarseOuter
        );

        builder.AddTriangleUpward(
            fine,
            coarseOuter,
            coarseZ
        );
    }

    // =====================================================
    // SAVE / UPDATE
    // =====================================================

    private static SaveResult SaveOrUpdateMesh(
        string assetPath,
        string meshName,
        GeneratedMeshData data
    )
    {
        Mesh mesh =
            AssetDatabase.LoadAssetAtPath<Mesh>(
                assetPath
            );

        bool isNew =
            mesh == null;

        if (isNew)
        {
            mesh =
                new Mesh();
        }
        else
        {
            /*
             * Update the existing asset in place so the
             * GUID remains stable.
             */

            mesh.Clear(
                false
            );
        }

        mesh.name =
            meshName;

        /*
         * Unity meshes default to 16-bit indices.
         *
         * Switch to UInt32 only if this generated asset
         * actually requires it.
         */
        mesh.indexFormat =
            data.vertices.Count >
                65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

        mesh.SetVertices(
            data.vertices
        );

        /*
         * Stage 3B clipmap-specific vertex data.
         *
         * UV channel 3 maps to the shader TEXCOORD3 semantic.
         *
         * clipmapData.x:
         *
         * 0 = coarse/stationary side
         * 1 = fine/adaptive side
         *
         * Center and ring meshes contain zero in this channel.
         */
        mesh.SetUVs(
            3,
            data.clipmapData
        );

        mesh.SetTriangles(
            data.triangles,
            0,
            true
        );

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // -------------------------------------------------
        // Verify clipmap data survived the mesh write
        // -------------------------------------------------

        List<Vector4> writtenClipmapData =
            new List<Vector4>();

        mesh.GetUVs(
            3,
            writtenClipmapData
        );

        if (
            writtenClipmapData.Count !=
            data.vertices.Count
        )
        {
            throw new InvalidOperationException(
                "Generated clipmap mesh did not preserve its " +
                "clipmap vertex data.\n\n" +

                $"Mesh: {meshName}\n" +
                $"Vertices: {data.vertices.Count}\n" +
                $"Clipmap Data Values: " +
                $"{writtenClipmapData.Count}"
            );
        }

        if (isNew)
        {
            AssetDatabase.CreateAsset(
                mesh,
                assetPath
            );

            return
                SaveResult.Created;
        }

        EditorUtility.SetDirty(
            mesh
        );

        AssetDatabase.SaveAssetIfDirty(
            mesh
        );

        return
            SaveResult.Updated;
    }

    // =====================================================
    // REMOVE OBSOLETE
    // =====================================================

    private static int RemoveObsoleteMeshes(
        HashSet<string> expectedAssetPaths
    )
    {
        int removedCount =
            0;

        if (
            !AssetDatabase.IsValidFolder(
                ClipmapMeshFolder
            )
        )
        {
            return
                removedCount;
        }

        string[] guids =
            AssetDatabase.FindAssets(
                "t:Mesh",
                new[]
                {
                    ClipmapMeshFolder
                }
            );

        foreach (string guid in guids)
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guid
                );

            string fileName =
                Path.GetFileNameWithoutExtension(
                    path
                );

            /*
             * Only manage assets owned by this generator.
             */

            if (
                !fileName.StartsWith(
                    "Clipmap_",
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            if (
                expectedAssetPaths.Contains(
                    path
                )
            )
            {
                continue;
            }

            if (
                AssetDatabase.DeleteAsset(
                    path
                )
            )
            {
                removedCount++;
            }
        }

        return
            removedCount;
    }

    // =====================================================
    // PATHS
    // =====================================================

    public static string GetCenterMeshPath()
    {
        return
            $"{ClipmapMeshFolder}/" +
            $"{GetCenterMeshName()}.asset";
    }

    public static string GetRingMeshPath(
        int level
    )
    {
        return
            $"{ClipmapMeshFolder}/" +
            $"{GetRingMeshName(level)}.asset";
    }

    public static string GetStitchMeshPath(
        int fineLevel,
        int coarseLevel
    )
    {
        return
            $"{ClipmapMeshFolder}/" +
            $"{GetStitchMeshName(fineLevel, coarseLevel)}" +
            ".asset";
    }

    // =====================================================
    // NAMES
    // =====================================================

    private static string GetCenterMeshName()
    {
        return
            "Clipmap_Center_LOD0";
    }

    private static string GetRingMeshName(
        int level
    )
    {
        return
            $"Clipmap_Ring_LOD{level}";
    }

    private static string GetStitchMeshName(
        int fineLevel,
        int coarseLevel
    )
    {
        return
            $"Clipmap_Stitch_LOD" +
            $"{fineLevel}_LOD{coarseLevel}";
    }

    // =====================================================
    // FOLDERS
    // =====================================================

    private static void EnsureFoldersExist()
    {
        // -------------------------------------------------
        // Generated
        // -------------------------------------------------

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.Generated
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Root,
                "Generated"
            );
        }

        // -------------------------------------------------
        // Generated/Meshes
        // -------------------------------------------------

        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.GeneratedMeshes
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.Generated,
                "Meshes"
            );
        }

        // -------------------------------------------------
        // Generated/Meshes/Clipmap
        // -------------------------------------------------

        if (
            !AssetDatabase.IsValidFolder(
                ClipmapMeshFolder
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.GeneratedMeshes,
                "Clipmap"
            );
        }
    }

    // =====================================================
    // PROGRESS
    // =====================================================

    private static bool ShowProgress(
        string operation,
        string detail,
        int current,
        int total
    )
    {
        float progress =
            total > 0
                ? (float)current /
                  total
                : 1f;

        return
            EditorUtility
                .DisplayCancelableProgressBar(
                    "Terrain Clipmap Generation",

                    operation +
                    "\n\n" +
                    detail,

                    progress
                );
    }

    // =====================================================
    // SAVE RESULT
    // =====================================================

    private enum SaveResult
    {
        Created,
        Updated
    }

    // =====================================================
    // GENERATED MESH DATA
    // =====================================================

    private sealed class GeneratedMeshData
    {
        public readonly List<Vector3> vertices;

        public readonly List<int> triangles;

        /*
         * Stored in mesh UV channel 3 / TEXCOORD3.
         *
         * x = adaptive stitch transition weight
         * yzw = reserved for future clipmap vertex data
         */
        public readonly List<Vector4> clipmapData;

        public GeneratedMeshData(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector4> clipmapData
        )
        {
            this.vertices =
                vertices;

            this.triangles =
                triangles;

            this.clipmapData =
                clipmapData;
        }
    }

    // =====================================================
    // MESH BUILDER
    // =====================================================

    private sealed class MeshBuilder
    {
        private readonly float spacing;

        /*
         * Negative means this is not a transition mesh.
         *
         * For a stitch mesh this is the Chebyshev radius,
         * measured in grid coordinates, of the boundary that
         * must later follow the finer LOD center.
         */
        private readonly int transitionFineBoundaryHalf;

        private readonly List<Vector3> vertices =
            new List<Vector3>();

        private readonly List<int> triangles =
            new List<int>();

        private readonly List<Vector4> clipmapData =
            new List<Vector4>();

        private readonly Dictionary<Vector2Int, int>
            vertexLookup =
                new Dictionary<Vector2Int, int>();

        public MeshBuilder(
            float spacing,
            int transitionFineBoundaryHalf = -1
        )
        {
            this.spacing =
                spacing;

            this.transitionFineBoundaryHalf =
                transitionFineBoundaryHalf;
        }

        // -------------------------------------------------
        // Get/add vertex
        // -------------------------------------------------

        public int GetVertex(
            Vector2Int gridCoordinate
        )
        {
            if (
                vertexLookup.TryGetValue(
                    gridCoordinate,
                    out int existingIndex
                )
            )
            {
                return
                    existingIndex;
            }

            int index =
                vertices.Count;

            Vector3 position =
                new Vector3(
                    gridCoordinate.x *
                    spacing,

                    0f,

                    gridCoordinate.y *
                    spacing
                );

            vertices.Add(
                position
            );

            float transitionWeight =
                CalculateTransitionWeight(
                    gridCoordinate
                );

            clipmapData.Add(
                new Vector4(
                    transitionWeight,
                    0f,
                    0f,
                    0f
                )
            );

            vertexLookup[
                gridCoordinate
            ] =
                index;

            return
                index;
        }

        // -------------------------------------------------
        // Transition weight
        // -------------------------------------------------

        private float CalculateTransitionWeight(
            Vector2Int gridCoordinate
        )
        {
            if (
                transitionFineBoundaryHalf <
                0
            )
            {
                return 0f;
            }

            int chebyshevRadius =
                Mathf.Max(
                    Mathf.Abs(
                        gridCoordinate.x
                    ),
                    Mathf.Abs(
                        gridCoordinate.y
                    )
                );

            return
                chebyshevRadius ==
                    transitionFineBoundaryHalf
                    ? 1f
                    : 0f;
        }

        // -------------------------------------------------
        // Unit grid quad
        // -------------------------------------------------

        public void AddGridQuad(
            int x,
            int z
        )
        {
            AddQuad(
                new Vector2Int(
                    x,
                    z
                ),

                new Vector2Int(
                    x + 1,
                    z
                ),

                new Vector2Int(
                    x + 1,
                    z + 1
                ),

                new Vector2Int(
                    x,
                    z + 1
                )
            );
        }

        // -------------------------------------------------
        // Arbitrary quad
        // -------------------------------------------------

        public void AddQuad(
            Vector2Int bottomLeft,
            Vector2Int bottomRight,
            Vector2Int topRight,
            Vector2Int topLeft
        )
        {
            int bl =
                GetVertex(
                    bottomLeft
                );

            int br =
                GetVertex(
                    bottomRight
                );

            int tr =
                GetVertex(
                    topRight
                );

            int tl =
                GetVertex(
                    topLeft
                );

            /*
             * Match the diagonal used by the existing
             * terrain/collision grid:
             *
             * bottomLeft -> topLeft -> bottomRight
             *
             * bottomRight -> topLeft -> topRight
             */

            AddTriangleUpward(
                bl,
                tl,
                br
            );

            AddTriangleUpward(
                br,
                tl,
                tr
            );
        }

        // -------------------------------------------------
        // Triangle with enforced upward winding
        // -------------------------------------------------

        public void AddTriangleUpward(
            int index0,
            int index1,
            int index2
        )
        {
            Vector3 p0 =
                vertices[
                    index0
                ];

            Vector3 p1 =
                vertices[
                    index1
                ];

            Vector3 p2 =
                vertices[
                    index2
                ];

            Vector3 cross =
                Vector3.Cross(
                    p1 - p0,
                    p2 - p0
                );

            if (
                Mathf.Abs(
                    cross.y
                )
                <=
                0.0000001f
            )
            {
                throw new InvalidOperationException(
                    "Clipmap generator attempted to create " +
                    "a degenerate triangle."
                );
            }

            if (cross.y < 0f)
            {
                int temporary =
                    index1;

                index1 =
                    index2;

                index2 =
                    temporary;
            }

            triangles.Add(
                index0
            );

            triangles.Add(
                index1
            );

            triangles.Add(
                index2
            );
        }

        // -------------------------------------------------
        // Validate all adaptive transition offsets
        // -------------------------------------------------

        private void ValidateAdaptiveTransitionTopology()
        {
            /*
             * Stage 3A proved that an adjacent fine/coarse LOD
             * pair can differ by only:
             *
             *     -S, 0, +S
             *
             * independently on X and Z.
             *
             * Validate all nine combinations here so generated
             * stitch topology can never become degenerate or
             * flip winding when Stage 3C activates movement.
             */
            for (
                int offsetZStep = -1;
                offsetZStep <= 1;
                offsetZStep++
            )
            {
                for (
                    int offsetXStep = -1;
                    offsetXStep <= 1;
                    offsetXStep++
                )
                {
                    Vector3 transitionOffset =
                        new Vector3(
                            offsetXStep *
                            spacing,

                            0f,

                            offsetZStep *
                            spacing
                        );

                    for (
                        int triangleOffset = 0;
                        triangleOffset < triangles.Count;
                        triangleOffset += 3
                    )
                    {
                        int index0 =
                            triangles[
                                triangleOffset
                            ];

                        int index1 =
                            triangles[
                                triangleOffset + 1
                            ];

                        int index2 =
                            triangles[
                                triangleOffset + 2
                            ];

                        Vector3 p0 =
                            vertices[
                                index0
                            ]
                            +
                            transitionOffset *
                            clipmapData[
                                index0
                            ].x;

                        Vector3 p1 =
                            vertices[
                                index1
                            ]
                            +
                            transitionOffset *
                            clipmapData[
                                index1
                            ].x;

                        Vector3 p2 =
                            vertices[
                                index2
                            ]
                            +
                            transitionOffset *
                            clipmapData[
                                index2
                            ].x;

                        float signedProjectedArea =
                            Vector3.Cross(
                                p1 - p0,
                                p2 - p0
                            ).y;

                        if (
                            signedProjectedArea <=
                            0.0000001f
                        )
                        {
                            throw new InvalidOperationException(
                                "Generated clipmap stitch becomes " +
                                "degenerate or flips winding for a " +
                                "valid adaptive LOD offset.\\n\\n" +

                                $"Offset: " +
                                $"({transitionOffset.x:R}, " +
                                $"{transitionOffset.z:R})\\n" +

                                $"Triangle: " +
                                $"{triangleOffset / 3}\\n" +

                                $"Signed Projected Area: " +
                                $"{signedProjectedArea:R}"
                            );
                        }
                    }
                }
            }
        }

        // -------------------------------------------------
        // Build
        // -------------------------------------------------

        public GeneratedMeshData Build()
        {
            if (vertices.Count == 0)
            {
                throw new InvalidOperationException(
                    "Generated clipmap mesh contains " +
                    "no vertices."
                );
            }

            if (
                triangles.Count == 0 ||
                triangles.Count % 3 != 0
            )
            {
                throw new InvalidOperationException(
                    "Generated clipmap mesh contains " +
                    "invalid triangle data."
                );
            }

            if (
                clipmapData.Count !=
                vertices.Count
            )
            {
                throw new InvalidOperationException(
                    "Generated clipmap vertex data count does " +
                    "not match the vertex count."
                );
            }

            if (
                transitionFineBoundaryHalf >=
                0
            )
            {
                int fineVertexCount =
                    0;

                int coarseVertexCount =
                    0;

                for (
                    int index = 0;
                    index < clipmapData.Count;
                    index++
                )
                {
                    float weight =
                        clipmapData[
                            index
                        ].x;

                    if (
                        Mathf.Approximately(
                            weight,
                            1f
                        )
                    )
                    {
                        fineVertexCount++;
                    }
                    else if (
                        Mathf.Approximately(
                            weight,
                            0f
                        )
                    )
                    {
                        coarseVertexCount++;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "Generated clipmap transition data " +
                            "contains a weight other than 0 or 1."
                        );
                    }
                }

                if (
                    fineVertexCount == 0
                    ||
                    coarseVertexCount == 0
                )
                {
                    throw new InvalidOperationException(
                        "Generated stitch mesh does not contain " +
                        "both fine and coarse transition vertices."
                    );
                }

                ValidateAdaptiveTransitionTopology();
            }

            return
                new GeneratedMeshData(
                    vertices,
                    triangles,
                    clipmapData
                );
        }
    }

}
