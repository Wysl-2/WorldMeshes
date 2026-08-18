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
        // Validate center resolution
        // -------------------------------------------------

        /*
         * The transition geometry assumes:
         *
         * fine spacing   = S
         * coarse spacing = 2S
         *
         * Requiring the center resolution to be divisible
         * by four ensures every ring boundary lands exactly
         * on both grids.
         */

        if (
            centerResolution < 8 ||
            centerResolution % 4 != 0
        )
        {
            Debug.LogError(
                "Cannot generate clipmap meshes.\n\n" +

                "Clipmap Center Resolution must be at " +
                "least 8 and evenly divisible by 4.\n\n" +

                $"Current Resolution: " +
                $"{centerResolution}"
            );

            return;
        }

        if (
            baseSpacing <= 0f ||
            float.IsNaN(baseSpacing) ||
            float.IsInfinity(baseSpacing)
        )
        {
            Debug.LogError(
                "Cannot generate clipmap meshes.\n\n" +

                "The derived clipmap base spacing is invalid."
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
                    baseSpacing *
                    Mathf.Pow(
                        2f,
                        level
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
                        centerResolution,
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
                    baseSpacing *
                    Mathf.Pow(
                        2f,
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
                        centerResolution,
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
            centerResolution *
            baseSpacing *
            Mathf.Pow(
                2f,
                levelCount - 1
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
        int centerResolution,
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
         * Outer half-extent:
         *
         * centerResolution / 2
         *
         * Inner half-extent:
         *
         * centerResolution / 4 + 1
         *
         * The extra one-cell gap is occupied by the
         * stitch mesh between this level and the previous
         * finer level.
         */

        int outerHalf =
            centerResolution /
            2;

        int innerHalf =
            centerResolution /
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
        int centerResolution,
        float fineSpacing
    )
    {
        MeshBuilder builder =
            new MeshBuilder(
                fineSpacing
            );

        /*
         * All coordinates here are measured in units of
         * the FINER level's spacing.
         *
         * The next coarse level has exactly twice this
         * spacing.
         */

        int half =
            centerResolution /
            2;

        const int coarseStep =
            2;

        int coarseSegmentsPerSide =
            centerResolution /
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
         * The four side strips leave one coarse-sized
         * square at each corner.
         *
         * Fill those four squares with simple quads.
         */

        // North-East
        builder.AddQuad(
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
        builder.AddQuad(
            new Vector2Int(
                -half - coarseStep,
                half
            ),

            new Vector2Int(
                -half,
                half
            ),

            new Vector2Int(
                -half,
                half + coarseStep
            ),

            new Vector2Int(
                -half - coarseStep,
                half + coarseStep
            )
        );

        // South-East
        builder.AddQuad(
            new Vector2Int(
                half,
                -half - coarseStep
            ),

            new Vector2Int(
                half + coarseStep,
                -half - coarseStep
            ),

            new Vector2Int(
                half + coarseStep,
                -half
            ),

            new Vector2Int(
                half,
                -half
            )
        );

        // South-West
        builder.AddQuad(
            new Vector2Int(
                -half - coarseStep,
                -half - coarseStep
            ),

            new Vector2Int(
                -half,
                -half - coarseStep
            ),

            new Vector2Int(
                -half,
                -half
            ),

            new Vector2Int(
                -half - coarseStep,
                -half
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

        mesh.SetTriangles(
            data.triangles,
            0,
            true
        );

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

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

        public GeneratedMeshData(
            List<Vector3> vertices,
            List<int> triangles
        )
        {
            this.vertices =
                vertices;

            this.triangles =
                triangles;
        }
    }

    // =====================================================
    // MESH BUILDER
    // =====================================================

    private sealed class MeshBuilder
    {
        private readonly float spacing;

        private readonly List<Vector3> vertices =
            new List<Vector3>();

        private readonly List<int> triangles =
            new List<int>();

        private readonly Dictionary<Vector2Int, int>
            vertexLookup =
                new Dictionary<Vector2Int, int>();

        public MeshBuilder(
            float spacing
        )
        {
            this.spacing =
                spacing;
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

            vertexLookup[
                gridCoordinate
            ] =
                index;

            return
                index;
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

            return
                new GeneratedMeshData(
                    vertices,
                    triangles
                );
        }
    }
}