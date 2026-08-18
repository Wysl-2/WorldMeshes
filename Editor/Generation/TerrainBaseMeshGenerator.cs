using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerrainBaseMeshGenerator
{
    private const string BaseMeshFolder =
        WorldMeshesPaths.GeneratedBaseMeshes;

    private const string LOD0Path =
        BaseMeshFolder + "/TerrainChunk_LOD0.asset";
    
    public static bool EnsureBaseMesh(
        float chunkSize,
        int resolution
    )
    {
        chunkSize =
            Mathf.Max(
                0.01f,
                chunkSize
            );

        resolution =
            Mathf.Max(
                1,
                resolution
            );

        EnsureFoldersExist();

        Mesh existingMesh =
            AssetDatabase.LoadAssetAtPath<Mesh>(
                LOD0Path
            );

        // No base mesh exists at all.
        if (existingMesh == null)
        {
            GenerateBaseMesh(
                chunkSize,
                resolution
            );

            return true;
        }

        // -------------------------
        // Check size
        // -------------------------

        const float tolerance =
            0.001f;

        bool sizeMatches =
            Mathf.Abs(
                existingMesh.bounds.size.x -
                chunkSize
            ) <= tolerance
            &&
            Mathf.Abs(
                existingMesh.bounds.size.z -
                chunkSize
            ) <= tolerance;

        // -------------------------
        // Check resolution
        // -------------------------

        /*
         * Our generated plane has:
         *
         * vertexCount =
         * (resolution + 1)^2
         *
         * So:
         *
         * resolution =
         * sqrt(vertexCount) - 1
         */

        int verticesPerSide =
            Mathf.RoundToInt(
                Mathf.Sqrt(
                    existingMesh.vertexCount
                )
            );

        bool validSquareGrid =
            verticesPerSide *
            verticesPerSide ==
            existingMesh.vertexCount;

        int existingResolution =
            verticesPerSide - 1;

        bool resolutionMatches =
            validSquareGrid &&
            existingResolution ==
            resolution;

        // -------------------------
        // Already correct
        // -------------------------

        if (
            sizeMatches &&
            resolutionMatches
        )
        {
            return false;
        }

        // -------------------------
        // Base mesh is outdated
        // -------------------------

        GenerateBaseMesh(
            chunkSize,
            resolution
        );

        return true;
    }

    public static void GenerateBaseMesh(
        float chunkSize,
        int resolution
    )
    {
        // -------------------------
        // Validate input
        // -------------------------

        chunkSize = Mathf.Max(
            0.01f,
            chunkSize
        );

        resolution = Mathf.Max(
            1,
            resolution
        );

        EnsureFoldersExist();

        // Generate the new mesh data.
        Mesh generatedMesh = CreatePlane(
            chunkSize,
            resolution
        );

        // Check whether the base mesh already exists.
        Mesh existingMesh =
            AssetDatabase.LoadAssetAtPath<Mesh>(
                LOD0Path
            );

        if (existingMesh != null)
        {
            // Replace the data inside the existing asset.
            //
            // This keeps the existing Mesh asset itself rather
            // than creating another duplicate asset.
            UpdateExistingMesh(
                existingMesh,
                generatedMesh
            );

            Object.DestroyImmediate(
                generatedMesh
            );

            EditorUtility.SetDirty(
                existingMesh
            );

            AssetDatabase.SaveAssets();

            Selection.activeObject =
                existingMesh;

            Debug.Log(
                $"Regenerated base terrain mesh:\n" +
                $"{LOD0Path}\n\n" +
                $"Chunk Size: {chunkSize}\n" +
                $"Resolution: {resolution}"
            );

            return;
        }

        // No mesh exists yet, so create the asset.
        AssetDatabase.CreateAsset(
            generatedMesh,
            LOD0Path
        );

        AssetDatabase.SaveAssets();

        Selection.activeObject =
            generatedMesh;

        Debug.Log(
            $"Created base terrain mesh:\n" +
            $"{LOD0Path}\n\n" +
            $"Chunk Size: {chunkSize}\n" +
            $"Resolution: {resolution}"
        );
    }

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
            // Generated/Meshes/Base
            // -------------------------------------------------

            if (
                !AssetDatabase.IsValidFolder(
                    BaseMeshFolder
                )
            )
            {
                AssetDatabase.CreateFolder(
                    WorldMeshesPaths.GeneratedMeshes,
                    "Base"
                );
            }
        }
    }

    private static Mesh CreatePlane(
        float size,
        int resolution
    )
    {
        Mesh mesh = new Mesh();

        mesh.name =
            "TerrainChunk_LOD0";

        int verticesPerSide =
            resolution + 1;

        int vertexCount =
            verticesPerSide *
            verticesPerSide;

        // A Mesh normally uses 16-bit indices.
        // Switch to 32-bit if the chosen resolution
        // produces more than 65,535 vertices.
        if (vertexCount > 65535)
        {
            mesh.indexFormat =
                IndexFormat.UInt32;
        }

        Vector3[] vertices =
            new Vector3[vertexCount];

        Vector2[] uvs =
            new Vector2[vertexCount];

        int[] triangles =
            new int[
                resolution *
                resolution *
                6
            ];

        // -------------------------
        // Vertices
        // -------------------------

        for (
            int z = 0;
            z <= resolution;
            z++
        )
        {
            for (
                int x = 0;
                x <= resolution;
                x++
            )
            {
                int index =
                    z * verticesPerSide + x;

                float xPercent =
                    (float)x / resolution;

                float zPercent =
                    (float)z / resolution;

                vertices[index] =
                    new Vector3(
                        xPercent * size,
                        0f,
                        zPercent * size
                    );

                uvs[index] =
                    new Vector2(
                        xPercent,
                        zPercent
                    );
            }
        }

        // -------------------------
        // Triangles
        // -------------------------

        int triangleIndex = 0;

        for (
            int z = 0;
            z < resolution;
            z++
        )
        {
            for (
                int x = 0;
                x < resolution;
                x++
            )
            {
                int bottomLeft =
                    z * verticesPerSide + x;

                int bottomRight =
                    bottomLeft + 1;

                int topLeft =
                    bottomLeft + verticesPerSide;

                int topRight =
                    topLeft + 1;

                // Triangle 1

                triangles[triangleIndex++] =
                    bottomLeft;

                triangles[triangleIndex++] =
                    topLeft;

                triangles[triangleIndex++] =
                    topRight;

                // Triangle 2

                triangles[triangleIndex++] =
                    bottomLeft;

                triangles[triangleIndex++] =
                    topRight;

                triangles[triangleIndex++] =
                    bottomRight;
            }
        }

        // -------------------------
        // Assign mesh data
        // -------------------------

        mesh.vertices =
            vertices;

        mesh.triangles =
            triangles;

        mesh.uv =
            uvs;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static void UpdateExistingMesh(
        Mesh existingMesh,
        Mesh generatedMesh
    )
    {
        existingMesh.Clear();

        existingMesh.indexFormat =
            generatedMesh.indexFormat;

        existingMesh.vertices =
            generatedMesh.vertices;

        existingMesh.triangles =
            generatedMesh.triangles;

        existingMesh.uv =
            generatedMesh.uv;

        existingMesh.RecalculateNormals();
        existingMesh.RecalculateBounds();

        existingMesh.name =
            "TerrainChunk_LOD0";
    }
}