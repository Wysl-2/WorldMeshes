using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainCollisionMeshGenerator
{
    // =====================================================
    // PATHS
    // =====================================================

    public const string CollisionMeshFolder =
        WorldMeshesPaths.GeneratedCollisionMeshes;

    // =====================================================
    // GENERATE COLLISION MESHES
    // =====================================================

    public static void GenerateCollisionMeshes(
        WorldSettings worldSettings
    )
    {
        // -------------------------------------------------
        // Validate
        // -------------------------------------------------

        if (worldSettings == null)
        {
            Debug.LogError(
                "Cannot generate collision meshes: " +
                "WorldSettings is null."
            );

            return;
        }

        if (
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            Debug.LogError(
                "Collision mesh generation must be " +
                "performed outside Play Mode."
            );

            return;
        }

        // -------------------------------------------------
        // Current layout
        // -------------------------------------------------

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

        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        int lod0Resolution =
            Mathf.Max(
                1,
                worldSettings.lod0Resolution
            );

        int collisionResolution =
            Mathf.Max(
                1,
                worldSettings.collisionResolution
            );

        int tileChunkSpan =
            Mathf.Max(
                1,
                worldSettings.heightTileChunkSpan
            );

        int tileGridWidth =
            worldSettings.HeightTileGridWidth;

        int tileGridHeight =
            worldSettings.HeightTileGridHeight;

        int samplesPerTile =
            worldSettings.HeightTileSamplesPerSide;

        // -------------------------------------------------
        // Validate collision resolution
        // -------------------------------------------------

        if (
            collisionResolution >
            lod0Resolution
        )
        {
            Debug.LogError(
                "Cannot generate collision meshes.\n\n" +

                $"LOD0 Resolution: " +
                $"{lod0Resolution}\n" +

                $"Collision Resolution: " +
                $"{collisionResolution}\n\n" +

                "Collision Resolution cannot be greater " +
                "than LOD0 Resolution."
            );

            return;
        }

        if (
            lod0Resolution %
            collisionResolution != 0
        )
        {
            Debug.LogError(
                "Cannot generate collision meshes.\n\n" +

                $"LOD0 Resolution: " +
                $"{lod0Resolution}\n" +

                $"Collision Resolution: " +
                $"{collisionResolution}\n\n" +

                "LOD0 Resolution must be evenly divisible " +
                "by Collision Resolution.\n\n" +

                "For LOD0 Resolution 128, examples include " +
                "128, 64, 32, 16, 8, 4, 2, or 1."
            );

            return;
        }

        int heightSampleStep =
            lod0Resolution /
            collisionResolution;

        float collisionVertexSpacing =
            chunkSize /
            collisionResolution;

        // -------------------------------------------------
        // Heightmap generation state
        // -------------------------------------------------

        if (
            TerrainGenerationStateUtility
                .GetHeightmapStatus(
                    worldSettings
                )
            !=
            TerrainGenerationStateUtility
                .GenerationStatus.Current
        )
        {
            Debug.LogError(
                "Cannot generate collision meshes.\n\n" +

                "The heightmaps are not current.\n\n" +

                "Generate the current heightmaps first."
            );

            return;
        }

        // -------------------------------------------------
        // Ensure output folder
        // -------------------------------------------------

        EnsureFoldersExist();

        // -------------------------------------------------
        // Existing collision meshes
        // -------------------------------------------------

        Dictionary<Vector2Int, Mesh>
            existingMeshes =
                FindExistingCollisionMeshes();

        int totalRequiredChunks =
            gridWidth *
            gridHeight;

        int totalOperations =
            existingMeshes.Count +
            totalRequiredChunks;

        int currentOperation =
            0;

        int createdCount =
            0;

        int updatedCount =
            0;

        int removedCount =
            0;

        int failedCount =
            0;

        bool cancelled =
            false;

        // =====================================================
        // GENERATION
        // =====================================================

        try
        {
            // =================================================
            // PHASE 1
            // Remove meshes outside current world grid
            // =================================================

            foreach (
                KeyValuePair<Vector2Int, Mesh> pair
                in existingMeshes
            )
            {
                Vector2Int coordinate =
                    pair.Key;

                cancelled =
                    ShowProgress(
                        "Checking existing collision meshes",

                        $"Chunk " +
                        $"({coordinate.x}, {coordinate.y})",

                        currentOperation,
                        totalOperations
                    );

                if (cancelled)
                {
                    break;
                }

                bool outsideGrid =
                    coordinate.x < 0 ||
                    coordinate.y < 0 ||
                    coordinate.x >= gridWidth ||
                    coordinate.y >= gridHeight;

                if (outsideGrid)
                {
                    string assetPath =
                        AssetDatabase.GetAssetPath(
                            pair.Value
                        );

                    if (
                        AssetDatabase.DeleteAsset(
                            assetPath
                        )
                    )
                    {
                        removedCount++;
                    }
                    else
                    {
                        Debug.LogError(
                            "Could not remove obsolete " +
                            "collision mesh:\n" +
                            assetPath
                        );

                        failedCount++;
                    }
                }

                currentOperation++;
            }

            // =================================================
            // PHASE 2
            // Generate by heightmap tile
            // =================================================

            if (
                !cancelled &&
                failedCount == 0
            )
            {
                for (
                    int tileZ = 0;
                    tileZ < tileGridHeight;
                    tileZ++
                )
                {
                    for (
                        int tileX = 0;
                        tileX < tileGridWidth;
                        tileX++
                    )
                    {
                        string heightTilePath =
                            TerrainHeightmapGenerator
                                .GetHeightTilePath(
                                    tileX,
                                    tileZ
                                );

                        Texture2D heightTile =
                            AssetDatabase
                                .LoadAssetAtPath<Texture2D>(
                                    heightTilePath
                                );

                        if (heightTile == null)
                        {
                            Debug.LogError(
                                "Required heightmap tile " +
                                "could not be loaded:\n" +
                                heightTilePath
                            );

                            failedCount++;

                            break;
                        }

                        if (
                            heightTile.width !=
                                samplesPerTile
                            ||
                            heightTile.height !=
                                samplesPerTile
                            ||
                            heightTile.format !=
                                TextureFormat.RFloat
                            ||
                            !heightTile.isReadable
                        )
                        {
                            Debug.LogError(
                                "Heightmap tile is invalid:\n" +
                                heightTilePath
                            );

                            failedCount++;

                            break;
                        }

                        NativeArray<float> heightData;

                        try
                        {
                            heightData =
                                heightTile
                                    .GetPixelData<float>(
                                        0
                                    );
                        }
                        catch (
                            System.Exception exception
                        )
                        {
                            Debug.LogError(
                                "Could not read heightmap tile:\n" +
                                heightTilePath +
                                "\n\n" +
                                exception.Message
                            );

                            failedCount++;

                            break;
                        }

                        int expectedHeightCount =
                            samplesPerTile *
                            samplesPerTile;

                        if (
                            heightData.Length !=
                            expectedHeightCount
                        )
                        {
                            Debug.LogError(
                                "Unexpected heightmap sample count.\n\n" +

                                $"Tile: " +
                                $"({tileX}, {tileZ})\n" +

                                $"Expected: " +
                                $"{expectedHeightCount:N0}\n" +

                                $"Actual: " +
                                $"{heightData.Length:N0}"
                            );

                            failedCount++;

                            break;
                        }

                        // -----------------------------------------
                        // Chunks covered by this tile
                        // -----------------------------------------

                        for (
                            int localChunkZ = 0;
                            localChunkZ < tileChunkSpan;
                            localChunkZ++
                        )
                        {
                            for (
                                int localChunkX = 0;
                                localChunkX < tileChunkSpan;
                                localChunkX++
                            )
                            {
                                int chunkX =
                                    tileX *
                                    tileChunkSpan +
                                    localChunkX;

                                int chunkZ =
                                    tileZ *
                                    tileChunkSpan +
                                    localChunkZ;

                                /*
                                 * Final heightmap tiles can extend
                                 * beyond the actual world grid.
                                 */
                                if (
                                    chunkX >= gridWidth ||
                                    chunkZ >= gridHeight
                                )
                                {
                                    continue;
                                }

                                cancelled =
                                    ShowProgress(
                                        "Generating collision meshes",

                                        $"Chunk " +
                                        $"({chunkX}, {chunkZ})\n" +

                                        $"{chunkX + chunkZ * gridWidth + 1} " +
                                        $"/ {totalRequiredChunks}",

                                        currentOperation,
                                        totalOperations
                                    );

                                if (cancelled)
                                {
                                    break;
                                }

                                string collisionPath =
                                    GetCollisionMeshPath(
                                        chunkX,
                                        chunkZ
                                    );

                                bool existedBefore =
                                    AssetDatabase
                                        .LoadAssetAtPath<Mesh>(
                                            collisionPath
                                        )
                                    != null;

                                bool success =
                                    GenerateOrUpdateCollisionMesh(
                                        chunkX,
                                        chunkZ,

                                        localChunkX,
                                        localChunkZ,

                                        chunkSize,

                                        lod0Resolution,
                                        collisionResolution,

                                        heightSampleStep,
                                        collisionVertexSpacing,

                                        samplesPerTile,
                                        heightData
                                    );

                                if (!success)
                                {
                                    failedCount++;

                                    break;
                                }

                                if (existedBefore)
                                {
                                    updatedCount++;
                                }
                                else
                                {
                                    createdCount++;
                                }

                                currentOperation++;
                            }

                            if (
                                cancelled ||
                                failedCount > 0
                            )
                            {
                                break;
                            }
                        }

                        if (
                            cancelled ||
                            failedCount > 0
                        )
                        {
                            break;
                        }
                    }

                    if (
                        cancelled ||
                        failedCount > 0
                    )
                    {
                        break;
                    }
                }
            }
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
                "Collision mesh generation cancelled.\n\n" +

                $"Created: {createdCount}\n" +
                $"Updated: {updatedCount}\n" +
                $"Removed: {removedCount}\n" +
                $"Failed: {failedCount}\n\n" +

                "Collision generation state was not " +
                "marked as current."
            );

            return;
        }

        // -------------------------------------------------
        // Failed
        // -------------------------------------------------

        if (failedCount > 0)
        {
            AssetDatabase.SaveAssets();

            Debug.LogError(
                "Collision mesh generation did not " +
                "complete successfully.\n\n" +

                $"Created: {createdCount}\n" +
                $"Updated: {updatedCount}\n" +
                $"Removed: {removedCount}\n" +
                $"Failed: {failedCount}\n\n" +

                "Collision generation state was not updated."
            );

            return;
        }

        // -------------------------------------------------
        // Record successful generation state
        // -------------------------------------------------

        bool stateRecorded =
            TerrainGenerationStateUtility
                .MarkCollisionMeshesGenerated(
                    worldSettings
                );

        if (!stateRecorded)
        {
            Debug.LogError(
                "Collision meshes were generated, but their " +
                "generation state could not be recorded."
            );

            return;
        }

        AssetDatabase.SaveAssets();

        // -------------------------------------------------
        // Complete
        // -------------------------------------------------

        Debug.Log(
            "Collision mesh generation complete.\n\n" +

            $"World Grid: " +
            $"{gridWidth} x {gridHeight}\n" +

            $"Chunk Size: " +
            $"{chunkSize}\n\n" +

            $"LOD0 Resolution: " +
            $"{lod0Resolution}\n" +

            $"Collision Resolution: " +
            $"{collisionResolution}\n" +

            $"Height Sample Step: " +
            $"{heightSampleStep}\n" +

            $"Collision Vertex Spacing: " +
            $"{collisionVertexSpacing}\n\n" +

            $"Vertices Per Collision Mesh: " +
            $"{(collisionResolution + 1) * (collisionResolution + 1):N0}\n\n" +

            $"Created: {createdCount}\n" +
            $"Updated: {updatedCount}\n" +
            $"Removed: {removedCount}\n" +
            $"Failed: {failedCount}\n\n" +

            $"Saved To:\n" +
            $"{CollisionMeshFolder}"
        );
    }

    // =====================================================
    // GENERATE / UPDATE ONE COLLISION MESH
    // =====================================================

    private static bool GenerateOrUpdateCollisionMesh(
    int chunkX,
    int chunkZ,

    int localChunkX,
    int localChunkZ,

    float chunkSize,

    int lod0Resolution,
    int collisionResolution,

    int heightSampleStep,
    float collisionVertexSpacing,

    int heightSamplesPerTile,
    NativeArray<float> heightData
)
{
    int verticesPerSide =
        collisionResolution + 1;

    int vertexCount =
        verticesPerSide *
        verticesPerSide;

    int triangleIndexCount =
        collisionResolution *
        collisionResolution *
        6;

    Vector3[] vertices =
        new Vector3[
            vertexCount
        ];

    int[] triangles =
        new int[
            triangleIndexCount
        ];

    // -------------------------------------------------
    // Source heightmap region
    // -------------------------------------------------

    int sourceStartX =
        localChunkX *
        lod0Resolution;

    int sourceStartZ =
        localChunkZ *
        lod0Resolution;

    // -------------------------------------------------
    // Vertices
    // -------------------------------------------------

    for (
        int z = 0;
        z <= collisionResolution;
        z++
    )
    {
        int sourceZ =
            sourceStartZ +
            z *
            heightSampleStep;

        for (
            int x = 0;
            x <= collisionResolution;
            x++
        )
        {
            int sourceX =
                sourceStartX +
                x *
                heightSampleStep;

            if (
                sourceX < 0 ||
                sourceX >= heightSamplesPerTile ||
                sourceZ < 0 ||
                sourceZ >= heightSamplesPerTile
            )
            {
                Debug.LogError(
                    "Collision mesh attempted to read " +
                    "outside the heightmap tile.\n\n" +

                    $"Chunk: ({chunkX}, {chunkZ})\n" +
                    $"Source Sample: " +
                    $"({sourceX}, {sourceZ})"
                );

                return false;
            }

            int sourceIndex =
                sourceZ *
                heightSamplesPerTile +
                sourceX;

            float height =
                heightData[
                    sourceIndex
                ];

            if (
                float.IsNaN(height) ||
                float.IsInfinity(height)
            )
            {
                Debug.LogError(
                    "Invalid height value encountered " +
                    "while generating collision mesh.\n\n" +

                    $"Chunk: ({chunkX}, {chunkZ})\n" +
                    $"Source Sample: " +
                    $"({sourceX}, {sourceZ})"
                );

                return false;
            }

            int vertexIndex =
                z *
                verticesPerSide +
                x;

            vertices[
                vertexIndex
            ] =
                new Vector3(
                    x *
                        collisionVertexSpacing,

                    height,

                    z *
                        collisionVertexSpacing
                );
        }
    }

    // -------------------------------------------------
    // Triangles
    // -------------------------------------------------

    int triangleIndex =
        0;

    for (
        int z = 0;
        z < collisionResolution;
        z++
    )
    {
        for (
            int x = 0;
            x < collisionResolution;
            x++
        )
        {
            int bottomLeft =
                z *
                verticesPerSide +
                x;

            int bottomRight =
                bottomLeft +
                1;

            int topLeft =
                bottomLeft +
                verticesPerSide;

            int topRight =
                topLeft +
                1;

            /*
             * Counter-clockwise when viewed from above.
             */

            triangles[
                triangleIndex++
            ] =
                bottomLeft;

            triangles[
                triangleIndex++
            ] =
                topLeft;

            triangles[
                triangleIndex++
            ] =
                bottomRight;

            triangles[
                triangleIndex++
            ] =
                bottomRight;

            triangles[
                triangleIndex++
            ] =
                topLeft;

            triangles[
                triangleIndex++
            ] =
                topRight;
        }
    }

    // -------------------------------------------------
    // Mesh asset
    // -------------------------------------------------

    string assetPath =
        GetCollisionMeshPath(
            chunkX,
            chunkZ
        );

    Mesh mesh =
        AssetDatabase
            .LoadAssetAtPath<Mesh>(
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
         * Update the existing asset in place so its
         * GUID and Addressables references are preserved.
         *
         * Clearing/modifying the geometry also invalidates
         * any previous physics bake. A new bake is performed
         * below after the replacement geometry is complete.
         */
        mesh.Clear(
            false
        );
    }

    mesh.name =
        GetCollisionMeshName(
            chunkX,
            chunkZ
        );

    mesh.indexFormat =
        vertexCount > 65535
            ? IndexFormat.UInt32
            : IndexFormat.UInt16;

    mesh.vertices =
        vertices;

    mesh.triangles =
        triangles;

    mesh.RecalculateBounds();

    // -------------------------------------------------
    // Verify horizontal dimensions
    // -------------------------------------------------

    float boundsTolerance =
        Mathf.Max(
            0.001f,
            chunkSize *
                0.00001f
        );

    if (
        Mathf.Abs(
            mesh.bounds.size.x -
            chunkSize
        ) >
        boundsTolerance
        ||
        Mathf.Abs(
            mesh.bounds.size.z -
            chunkSize
        ) >
        boundsTolerance
    )
    {
        Debug.LogError(
            "Generated collision mesh has incorrect " +
            "horizontal bounds.\n\n" +

            $"Chunk: ({chunkX}, {chunkZ})\n" +

            $"Expected X/Z Size: " +
            $"{chunkSize}\n" +

            $"Actual Size: " +
            $"{mesh.bounds.size}"
        );

        if (isNew)
        {
            Object.DestroyImmediate(
                mesh
            );
        }

        return false;
    }

    // -------------------------------------------------
    // Make Mesh persistent before physics baking
    // -------------------------------------------------

    if (isNew)
    {
        AssetDatabase.CreateAsset(
            mesh,
            assetPath
        );
    }

    // -------------------------------------------------
    // Pre-bake PhysX collision data
    // -------------------------------------------------

    if (
        !BakeCollisionMesh(
            mesh,
            chunkX,
            chunkZ
        )
    )
    {
        /*
         * A newly created asset should not be left behind
         * as though generation succeeded if its physics
         * data could not be baked.
         */
        if (isNew)
        {
            AssetDatabase.DeleteAsset(
                assetPath
            );
        }

        return false;
    }

    // -------------------------------------------------
    // Save geometry + baked physics data
    // -------------------------------------------------

    EditorUtility.SetDirty(
        mesh
    );

    AssetDatabase.SaveAssetIfDirty(
        mesh
    );

    return true;
}
    
    private static bool BakeCollisionMesh(
        Mesh mesh,
        int chunkX,
        int chunkZ
    )
    {
        if (mesh == null)
        {
            Debug.LogError(
                "Cannot bake collision mesh physics data.\n\n" +
                $"Chunk: ({chunkX}, {chunkZ})\n" +
                "Mesh is null."
            );

            return false;
        }

        try
        {
            Physics.BakeMesh(
                mesh.GetInstanceID(),
                TerrainCollisionPhysicsSettings.Convex,
                TerrainCollisionPhysicsSettings.CookingOptions
            );
        }
        catch (
            System.Exception exception
        )
        {
            Debug.LogError(
                "Failed to pre-bake collision mesh physics data.\n\n" +

                $"Chunk: ({chunkX}, {chunkZ})\n" +
                $"Mesh: {mesh.name}\n\n" +

                exception.Message
            );

            return false;
        }

        return true;
    }

    // =====================================================
    // FIND EXISTING COLLISION MESHES
    // =====================================================

    private static Dictionary<Vector2Int, Mesh>
        FindExistingCollisionMeshes()
    {
        Dictionary<Vector2Int, Mesh> meshes =
            new Dictionary<Vector2Int, Mesh>();

        if (
            !AssetDatabase.IsValidFolder(
                CollisionMeshFolder
            )
        )
        {
            return meshes;
        }

        string[] guids =
            AssetDatabase.FindAssets(
                "t:Mesh",
                new[]
                {
                    CollisionMeshFolder
                }
            );

        foreach (string guid in guids)
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guid
                );

            if (
                !TryGetCollisionCoordinates(
                    path,
                    out int x,
                    out int z
                )
            )
            {
                continue;
            }

            Mesh mesh =
                AssetDatabase.LoadAssetAtPath<Mesh>(
                    path
                );

            if (mesh == null)
            {
                continue;
            }

            meshes[
                new Vector2Int(
                    x,
                    z
                )
            ] =
                mesh;
        }

        return meshes;
    }

    // =====================================================
    // PATH / NAME
    // =====================================================

    public static string GetCollisionMeshPath(
        int chunkX,
        int chunkZ
    )
    {
        return
            $"{CollisionMeshFolder}/" +
            $"{GetCollisionMeshName(chunkX, chunkZ)}" +
            ".asset";
    }

    private static string GetCollisionMeshName(
        int chunkX,
        int chunkZ
    )
    {
        return
            $"Chunk_{chunkX}_{chunkZ}_Collision";
    }

    // =====================================================
    // PARSE COORDINATES
    // =====================================================

    private static bool TryGetCollisionCoordinates(
        string assetPath,
        out int x,
        out int z
    )
    {
        x =
            0;

        z =
            0;

        string fileName =
            Path.GetFileNameWithoutExtension(
                assetPath
            );

        /*
         * Expected:
         *
         * Chunk_12_7_Collision
         */

        string[] parts =
            fileName.Split(
                '_'
            );

        if (parts.Length != 4)
        {
            return false;
        }

        if (
            parts[0] !=
            "Chunk"
            ||
            parts[3] !=
            "Collision"
        )
        {
            return false;
        }

        if (
            !int.TryParse(
                parts[1],
                out x
            )
        )
        {
            return false;
        }

        if (
            !int.TryParse(
                parts[2],
                out z
            )
        )
        {
            return false;
        }

        return true;
    }

    // =====================================================
    // FOLDERS
    // =====================================================

    private static void EnsureFoldersExist()
    {
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
            // Generated/Meshes/Collision
            // -------------------------------------------------

            if (
                !AssetDatabase.IsValidFolder(
                    CollisionMeshFolder
                )
            )
            {
                AssetDatabase.CreateFolder(
                    WorldMeshesPaths.GeneratedMeshes,
                    "Collision"
                );
            }
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
                    "Terrain Collision Generation",

                    operation +
                    "\n\n" +
                    detail,

                    progress
                );
    }
}