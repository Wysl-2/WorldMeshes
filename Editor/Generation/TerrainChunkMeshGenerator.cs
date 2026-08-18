using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainChunkMeshGenerator
{
    private const string BaseMeshPath =
        WorldMeshesPaths.BaseMeshAssetPath;

    private const string ChunkMeshFolder =
        WorldMeshesPaths.GeneratedChunkMeshes;

    // =====================================================
    // SYNC CHUNK MESHES
    // =====================================================

public static void SyncChunkMeshes(
    WorldSettings worldSettings
)
{
    // -------------------------------------------------
    // Validate WorldSettings
    // -------------------------------------------------

    if (worldSettings == null)
    {
        Debug.LogError(
            "Cannot synchronize chunk meshes: " +
            "WorldSettings is null."
        );

        return;
    }

    float currentChunkSize =
        Mathf.Max(
            0.01f,
            worldSettings.chunkSize
        );

    int currentLOD0Resolution =
        Mathf.Max(
            1,
            worldSettings.lod0Resolution
        );

    // -------------------------------------------------
    // Previous successful generation settings
    // -------------------------------------------------

    float previousChunkSize =
        worldSettings.lastSyncedChunkSize;

    int previousLOD0Resolution =
        worldSettings.lastSyncedLOD0Resolution;

    // -------------------------------------------------
    // Load base mesh
    // -------------------------------------------------

    Mesh baseMesh =
        AssetDatabase.LoadAssetAtPath<Mesh>(
            BaseMeshPath
        );

    if (baseMesh == null)
    {
        Debug.LogError(
            "Cannot synchronize chunk meshes.\n\n" +
            "LOD0 base mesh does not exist.\n\n" +
            "Generate the LOD0 base mesh first."
        );

        return;
    }

    // -------------------------------------------------
    // Validate base mesh size
    // -------------------------------------------------

    const float sizeTolerance =
        0.001f;

    float baseSizeX =
        baseMesh.bounds.size.x;

    float baseSizeZ =
        baseMesh.bounds.size.z;

    bool baseSizeMatches =
        Mathf.Abs(
            baseSizeX -
            currentChunkSize
        ) <= sizeTolerance
        &&
        Mathf.Abs(
            baseSizeZ -
            currentChunkSize
        ) <= sizeTolerance;

    if (!baseSizeMatches)
    {
        Debug.LogError(
            "Cannot synchronize chunk meshes.\n\n" +
            "The LOD0 base mesh does not match the " +
            "current WorldSettings chunk size.\n\n" +

            $"WorldSettings Chunk Size: " +
            $"{currentChunkSize}\n" +

            $"Base Mesh Size: " +
            $"{baseSizeX} x {baseSizeZ}\n\n" +

            "Generate the LOD0 base mesh again first."
        );

        return;
    }

    // -------------------------------------------------
    // Validate base mesh resolution
    // -------------------------------------------------

    int expectedVerticesPerSide =
        currentLOD0Resolution + 1;

    int expectedVertexCount =
        expectedVerticesPerSide *
        expectedVerticesPerSide;

    if (
        baseMesh.vertexCount !=
        expectedVertexCount
    )
    {
        Debug.LogError(
            "Cannot synchronize chunk meshes.\n\n" +
            "The LOD0 base mesh does not match the " +
            "current WorldSettings LOD0 resolution.\n\n" +

            $"WorldSettings Resolution: " +
            $"{currentLOD0Resolution}\n" +

            $"Expected Vertex Count: " +
            $"{expectedVertexCount:N0}\n" +

            $"Base Mesh Vertex Count: " +
            $"{baseMesh.vertexCount:N0}\n\n" +

            "Generate the LOD0 base mesh again first."
        );

        return;
    }

    // -------------------------------------------------
    // Detect generation-setting changes
    // -------------------------------------------------

    bool chunkSizeChanged =
        Mathf.Abs(
            previousChunkSize -
            currentChunkSize
        ) > sizeTolerance;

    bool resolutionChanged =
        previousLOD0Resolution !=
        currentLOD0Resolution;

    bool generationSettingsChanged =
        chunkSizeChanged ||
        resolutionChanged;

    // -------------------------------------------------
    // Base mesh hash
    // -------------------------------------------------

    string currentBaseHash =
        AssetDatabase
            .GetAssetDependencyHash(
                BaseMeshPath
            )
            .ToString();

    bool baseHashChanged =
        worldSettings.lastSyncedBaseMeshHash
        != currentBaseHash;

    bool forceRebuildAllExisting =
        generationSettingsChanged ||
        baseHashChanged;

    // -------------------------------------------------
    // Ensure chunk folder
    // -------------------------------------------------

    EnsureChunkFolderExists();

    // -------------------------------------------------
    // World dimensions
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

    int totalRequiredChunks =
        gridWidth *
        gridHeight;

    // -------------------------------------------------
    // Existing meshes
    // -------------------------------------------------

    Dictionary<Vector2Int, Mesh> existingMeshes =
        FindExistingChunkMeshes();

    // -------------------------------------------------
    // Statistics
    // -------------------------------------------------

    int createdCount = 0;
    int updatedCount = 0;
    int removedCount = 0;
    int unchangedCount = 0;
    int failedCount = 0;

    bool cancelled = false;

    int totalOperations =
        existingMeshes.Count +
        totalRequiredChunks;

    int currentOperation = 0;

    // -------------------------------------------------
    // Synchronize
    // -------------------------------------------------

    try
    {
        // =================================================
        // PHASE 1
        // Remove obsolete meshes
        // =================================================

        foreach (
            KeyValuePair<Vector2Int, Mesh> pair
            in existingMeshes
        )
        {
            Vector2Int coordinate =
                pair.Key;

            Mesh mesh =
                pair.Value;

            float progress =
                totalOperations > 0
                    ? (float)currentOperation /
                      totalOperations
                    : 1f;

            cancelled =
                EditorUtility
                    .DisplayCancelableProgressBar(
                        "Synchronizing Chunk Meshes",

                        $"Checking existing chunk " +
                        $"({coordinate.x}, " +
                        $"{coordinate.y})",

                        progress
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
                string meshPath =
                    AssetDatabase.GetAssetPath(
                        mesh
                    );

                if (
                    AssetDatabase.DeleteAsset(
                        meshPath
                    )
                )
                {
                    removedCount++;
                }
            }

            currentOperation++;
        }

        // =================================================
        // PHASE 2
        // Create/update required meshes
        // =================================================

        if (!cancelled)
        {
            for (
                int y = 0;
                y < gridHeight;
                y++
            )
            {
                for (
                    int x = 0;
                    x < gridWidth;
                    x++
                )
                {
                    float progress =
                        totalOperations > 0
                            ? (float)currentOperation /
                              totalOperations
                            : 1f;

                    cancelled =
                        EditorUtility
                            .DisplayCancelableProgressBar(
                                "Synchronizing Chunk Meshes",

                                $"Chunk ({x}, {y})\n" +
                                $"{x + y * gridWidth + 1} " +
                                $"/ {totalRequiredChunks}",

                                progress
                            );

                    if (cancelled)
                    {
                        break;
                    }

                    Vector2Int coordinate =
                        new Vector2Int(
                            x,
                            y
                        );

                    string chunkPath =
                        GetChunkMeshPath(
                            x,
                            y
                        );

                    // -----------------------------------------
                    // Existing chunk
                    // -----------------------------------------

                    if (
                        existingMeshes.TryGetValue(
                            coordinate,
                            out Mesh existingMesh
                        )
                        &&
                        existingMesh != null
                    )
                    {
                        bool meshStructureMismatch =
                            !ChunkMeshMatchesBase(
                                existingMesh,
                                baseMesh
                            );

                        bool meshNeedsUpdate =
                            forceRebuildAllExisting ||
                            meshStructureMismatch;

                        if (meshNeedsUpdate)
                        {
                            bool updateSucceeded =
                                UpdateChunkFromBaseMesh(
                                    baseMesh,
                                    existingMesh,
                                    x,
                                    y
                                );

                            if (updateSucceeded)
                            {
                                AssetDatabase.SaveAssetIfDirty(
                                    existingMesh
                                );

                                updatedCount++;
                            }
                            else
                            {
                                failedCount++;
                            }
                        }
                        else
                        {
                            unchangedCount++;
                        }
                    }

                    // -----------------------------------------
                    // Missing chunk
                    // -----------------------------------------

                    else
                    {
                        bool success =
                            AssetDatabase.CopyAsset(
                                BaseMeshPath,
                                chunkPath
                            );

                        if (!success)
                        {
                            Debug.LogError(
                                "Failed to create chunk mesh:\n" +
                                chunkPath
                            );

                            failedCount++;
                        }
                        else
                        {
                            Mesh newChunkMesh =
                                AssetDatabase.LoadAssetAtPath<Mesh>(
                                    chunkPath
                                );

                            if (newChunkMesh == null)
                            {
                                Debug.LogError(
                                    "Chunk mesh asset was created " +
                                    "but could not be loaded:\n" +
                                    chunkPath
                                );

                                failedCount++;
                            }
                            else
                            {
                                newChunkMesh.name =
                                    $"Chunk_{x}_{y}_LOD0";

                                EditorUtility.SetDirty(
                                    newChunkMesh
                                );

                                AssetDatabase.SaveAssetIfDirty(
                                    newChunkMesh
                                );

                                // ---------------------------------
                                // Verify newly created mesh
                                // ---------------------------------

                                if (
                                    ChunkMeshMatchesBase(
                                        newChunkMesh,
                                        baseMesh
                                    )
                                )
                                {
                                    createdCount++;
                                }
                                else
                                {
                                    Debug.LogError(
                                        "Newly created chunk mesh " +
                                        "does not match the base mesh:\n" +
                                        chunkPath
                                    );

                                    failedCount++;
                                }
                            }
                        }
                    }

                    currentOperation++;
                }

                if (cancelled)
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
        Debug.LogWarning(
            "Chunk mesh synchronization cancelled.\n\n" +

            $"Created: {createdCount}\n" +
            $"Updated: {updatedCount}\n" +
            $"Removed: {removedCount}\n" +
            $"Unchanged: {unchangedCount}\n" +
            $"Failed: {failedCount}\n\n" +

            "Generation state was not marked as synchronized."
        );

        return;
    }

    // -------------------------------------------------
    // Failed updates
    // -------------------------------------------------

    if (failedCount > 0)
    {
        /*
         * Do NOT mark the generation state synchronized.
         *
         * This guarantees another Sync attempt will still
         * regard the generated meshes as stale.
         */

        Debug.LogError(
            "Chunk mesh synchronization did not complete " +
            "successfully.\n\n" +

            $"Created: {createdCount}\n" +
            $"Updated: {updatedCount}\n" +
            $"Removed: {removedCount}\n" +
            $"Unchanged: {unchangedCount}\n" +
            $"Failed: {failedCount}\n\n" +

            "The synchronization state was not updated."
        );

        return;
    }

    // -------------------------------------------------
    // Record successful synchronization
    // -------------------------------------------------

    bool chunkAssetsChanged =
        createdCount > 0
        ||
        updatedCount > 0
        ||
        removedCount > 0;

    TerrainGenerationStateUtility
        .MarkChunkSyncSuccessful(
            worldSettings,
            currentBaseHash,
            chunkAssetsChanged
        );

    AssetDatabase.SaveAssets();

    // -------------------------------------------------
    // Complete
    // -------------------------------------------------

    Debug.Log(
        "Chunk mesh synchronization complete.\n\n" +

        $"World Grid: " +
        $"{gridWidth} x {gridHeight}\n\n" +

        $"Base Mesh Size: " +
        $"{baseMesh.bounds.size.x} x " +
        $"{baseMesh.bounds.size.z}\n" +

        $"Base Mesh Vertices: " +
        $"{baseMesh.vertexCount:N0}\n\n" +

        $"Current Chunk Size: " +
        $"{currentChunkSize}\n" +

        $"Previous Synced Chunk Size: " +
        $"{previousChunkSize}\n" +

        $"Chunk Size Changed: " +
        $"{chunkSizeChanged}\n\n" +

        $"Current LOD0 Resolution: " +
        $"{currentLOD0Resolution}\n" +

        $"Previous Synced Resolution: " +
        $"{previousLOD0Resolution}\n" +

        $"Resolution Changed: " +
        $"{resolutionChanged}\n\n" +

        $"Base Hash Changed: " +
        $"{baseHashChanged}\n" +

        $"Force Rebuild Existing: " +
        $"{forceRebuildAllExisting}\n\n" +

        $"Created: {createdCount}\n" +
        $"Updated: {updatedCount}\n" +
        $"Removed: {removedCount}\n" +
        $"Unchanged: {unchangedCount}\n" +
        $"Failed: {failedCount}"
    );
}

private static bool ChunkMeshMatchesBase(
    Mesh chunkMesh,
    Mesh baseMesh
)
{
    if (
        chunkMesh == null ||
        baseMesh == null
    )
    {
        return false;
    }

    const float tolerance =
        0.001f;

    // -------------------------------------------------
    // Horizontal dimensions
    // -------------------------------------------------

    bool sizeMatches =
        Mathf.Abs(
            chunkMesh.bounds.size.x -
            baseMesh.bounds.size.x
        ) <= tolerance
        &&
        Mathf.Abs(
            chunkMesh.bounds.size.z -
            baseMesh.bounds.size.z
        ) <= tolerance;

    if (!sizeMatches)
    {
        return false;
    }

    // -------------------------------------------------
    // Vertex count
    // -------------------------------------------------

    if (
        chunkMesh.vertexCount !=
        baseMesh.vertexCount
    )
    {
        return false;
    }

    // -------------------------------------------------
    // Submeshes
    // -------------------------------------------------

    if (
        chunkMesh.subMeshCount !=
        baseMesh.subMeshCount
    )
    {
        return false;
    }

    // -------------------------------------------------
    // Index counts
    // -------------------------------------------------

    for (
        int i = 0;
        i < baseMesh.subMeshCount;
        i++
    )
    {
        if (
            chunkMesh.GetIndexCount(i) !=
            baseMesh.GetIndexCount(i)
        )
        {
            return false;
        }
    }

    return true;
}

    // =====================================================
    // UPDATE EXISTING CHUNK
    // =====================================================

    private static bool UpdateChunkFromBaseMesh(
    Mesh baseMesh,
    Mesh chunkMesh,
    int x,
    int y
)
{
    // -------------------------------------------------
    // Validate
    // -------------------------------------------------

    if (
        baseMesh == null ||
        chunkMesh == null
    )
    {
        return false;
    }

    // -------------------------------------------------
    // Copy source data BEFORE clearing destination
    // -------------------------------------------------

    Vector3[] vertices =
        baseMesh.vertices;

    int[] triangles =
        baseMesh.triangles;

    Vector2[] uvs =
        baseMesh.uv;

    IndexFormat indexFormat =
        baseMesh.indexFormat;

    // -------------------------------------------------
    // Completely clear existing mesh
    // -------------------------------------------------

    /*
     * false means do not preserve the previous
     * vertex-data layout.
     *
     * This is important when the new base mesh has a
     * different resolution or vertex configuration.
     */

    chunkMesh.Clear(
        false
    );

    // -------------------------------------------------
    // Rebuild from current base mesh
    // -------------------------------------------------

    chunkMesh.indexFormat =
        indexFormat;

    chunkMesh.vertices =
        vertices;

    chunkMesh.triangles =
        triangles;

    chunkMesh.uv =
        uvs;

    // -------------------------------------------------
    // Recalculate derived data
    // -------------------------------------------------

    chunkMesh.RecalculateNormals();
    chunkMesh.RecalculateBounds();

    // -------------------------------------------------
    // Restore unique chunk name
    // -------------------------------------------------

    chunkMesh.name =
        $"Chunk_{x}_{y}_LOD0";

    // -------------------------------------------------
    // Mark asset dirty
    // -------------------------------------------------

    EditorUtility.SetDirty(
        chunkMesh
    );

    // -------------------------------------------------
    // Verify update
    // -------------------------------------------------

    const float tolerance =
        0.001f;

    bool sizeMatches =
        Mathf.Abs(
            chunkMesh.bounds.size.x -
            baseMesh.bounds.size.x
        ) <= tolerance
        &&
        Mathf.Abs(
            chunkMesh.bounds.size.z -
            baseMesh.bounds.size.z
        ) <= tolerance;

    bool vertexCountMatches =
        chunkMesh.vertexCount ==
        baseMesh.vertexCount;

    bool indexCountMatches =
        chunkMesh.triangles.Length ==
        baseMesh.triangles.Length;

    if (
        !sizeMatches ||
        !vertexCountMatches ||
        !indexCountMatches
    )
    {
        Debug.LogError(
            $"Failed to update chunk mesh " +
            $"({x}, {y}).\n\n" +

            $"Base Size: " +
            $"{baseMesh.bounds.size.x} x " +
            $"{baseMesh.bounds.size.z}\n" +

            $"Chunk Size After Update: " +
            $"{chunkMesh.bounds.size.x} x " +
            $"{chunkMesh.bounds.size.z}\n\n" +

            $"Base Vertices: " +
            $"{baseMesh.vertexCount}\n" +

            $"Chunk Vertices After Update: " +
            $"{chunkMesh.vertexCount}"
        );

        return false;
    }

    return true;
}

    // =====================================================
    // FIND GENERATED CHUNKS
    // =====================================================

    private static Dictionary<Vector2Int, Mesh>
        FindExistingChunkMeshes()
    {
        Dictionary<Vector2Int, Mesh> meshes =
            new Dictionary<Vector2Int, Mesh>();

        if (
            !AssetDatabase.IsValidFolder(
                ChunkMeshFolder
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
                    ChunkMeshFolder
                }
            );

        foreach (string guid in guids)
        {
            string path =
                AssetDatabase.GUIDToAssetPath(
                    guid
                );

            if (
                !TryGetChunkCoordinates(
                    path,
                    out int x,
                    out int y
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
                    y
                )
            ] = mesh;
        }

        return meshes;
    }

    // =====================================================
    // PATH
    // =====================================================

    private static string GetChunkMeshPath(
        int x,
        int y
    )
    {
        return
            $"{ChunkMeshFolder}/" +
            $"Chunk_{x}_{y}_LOD0.asset";
    }

    // =====================================================
    // PARSE COORDINATES
    // =====================================================

    private static bool TryGetChunkCoordinates(
        string assetPath,
        out int x,
        out int y
    )
    {
        x = 0;
        y = 0;

        string fileName =
            Path.GetFileNameWithoutExtension(
                assetPath
            );

        /*
         * Expected:
         *
         * Chunk_12_7_LOD0
         */

        string[] parts =
            fileName.Split('_');

        if (parts.Length != 4)
        {
            return false;
        }

        if (parts[0] != "Chunk")
        {
            return false;
        }

        if (parts[3] != "LOD0")
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
                out y
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

    private static void EnsureChunkFolderExists()
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
        // Generated/Meshes/Chunks
        // -------------------------------------------------

        if (
            !AssetDatabase.IsValidFolder(
                ChunkMeshFolder
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.GeneratedMeshes,
                "Chunks"
            );
        }
    }
}