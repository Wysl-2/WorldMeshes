using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Generates persistent editor-only explicit-edge wireframe preview meshes
 * alongside the normal clipmap mesh generation workflow.
 *
 * Each source clipmap mesh owns one preview asset file. The asset contains
 * one Mesh object per non-empty spatial section. Every section uses:
 *
 * submesh 0 = exact triangle topology for Wireframe Only depth
 * submesh 1 = deduplicated explicit triangle edges with MeshTopology.Lines
 *
 * Generated preview meshes contain only position and TEXCOORD3 clipmap
 * transition data. Height displacement remains entirely shader-driven.
 */
public static class TerrainClipmapWireframePreviewGenerator
{
    // =====================================================
    // SECTION POLICY
    // =====================================================

    private const int TargetTrianglesPerSection =
        20000;

    private const int MaximumSectionsPerAxis =
        8;

    private const string PreviewAssetPrefix =
        "Wireframe_";

    // =====================================================
    // PATHS
    // =====================================================

    public static string GetPreviewAssetPath(
        string sourceMeshName
    )
    {
        if (
            string.IsNullOrEmpty(
                sourceMeshName
            )
        )
        {
            return
                "";
        }

        return
            $"{WorldMeshesPaths.GeneratedWireframePreviewMeshes}/" +
            $"{PreviewAssetPrefix}{sourceMeshName}.asset";
    }

    // =====================================================
    // GENERATE SOURCE PREVIEW
    // =====================================================

    public static int GeneratePreview(
        string sourceMeshName,
        List<Vector3> sourceVertices,
        List<int> sourceTriangles,
        List<Vector4> sourceClipmapData,
        HashSet<string> expectedPreviewAssetPaths
    )
    {
        ValidateSourceData(
            sourceMeshName,
            sourceVertices,
            sourceTriangles,
            sourceClipmapData
        );

        EnsureFolderExists();

        string assetPath =
            GetPreviewAssetPath(
                sourceMeshName
            );

        if (
            expectedPreviewAssetPaths !=
            null
        )
        {
            expectedPreviewAssetPaths.Add(
                assetPath
            );
        }

        List<WireframeSectionData> sections =
            BuildSections(
                sourceMeshName,
                sourceVertices,
                sourceTriangles,
                sourceClipmapData
            );

        if (sections.Count <= 0)
        {
            throw new InvalidOperationException(
                $"Generated wireframe preview for '{sourceMeshName}' " +
                "contains no non-empty sections."
            );
        }

        SaveOrUpdatePreviewAsset(
            assetPath,
            sourceMeshName,
            sections
        );

        return
            sections.Count;
    }

    // =====================================================
    // REMOVE OBSOLETE PREVIEWS
    // =====================================================

    public static int RemoveObsoletePreviewAssets(
        HashSet<string> expectedPreviewAssetPaths
    )
    {
        int removedCount =
            0;

        string previewFolder =
            WorldMeshesPaths.GeneratedWireframePreviewMeshes;

        if (
            !AssetDatabase.IsValidFolder(
                previewFolder
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
                    previewFolder
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

            if (
                !fileName.StartsWith(
                    PreviewAssetPrefix,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            if (
                expectedPreviewAssetPaths != null
                &&
                expectedPreviewAssetPaths.Contains(
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
    // SECTION BUILD
    // =====================================================

    private static List<WireframeSectionData> BuildSections(
        string sourceMeshName,
        List<Vector3> sourceVertices,
        List<int> sourceTriangles,
        List<Vector4> sourceClipmapData
    )
    {
        int triangleCount =
            sourceTriangles.Count /
            3;

        int sectionsPerAxis =
            ResolveSectionsPerAxis(
                triangleCount
            );

        Bounds sourceBounds =
            CalculateBounds(
                sourceVertices
            );

        int sectionCount =
            sectionsPerAxis *
            sectionsPerAxis;

        List<int>[] sectionTriangles =
            new List<int>[
                sectionCount
            ];

        int estimatedIndicesPerSection =
            Mathf.Max(
                96,
                sourceTriangles.Count /
                    Mathf.Max(
                        1,
                        sectionCount
                    )
            );

        for (
            int index = 0;
            index + 2 < sourceTriangles.Count;
            index += 3
        )
        {
            int a =
                sourceTriangles[index];

            int b =
                sourceTriangles[index + 1];

            int c =
                sourceTriangles[index + 2];

            ValidateTriangleIndices(
                sourceMeshName,
                a,
                b,
                c,
                sourceVertices.Count
            );

            Vector3 centroid =
                (
                    sourceVertices[a] +
                    sourceVertices[b] +
                    sourceVertices[c]
                ) /
                3f;

            int sectionIndex =
                ResolveSectionIndex(
                    sourceBounds,
                    sectionsPerAxis,
                    centroid
                );

            List<int> indices =
                sectionTriangles[
                    sectionIndex
                ];

            if (indices == null)
            {
                indices =
                    new List<int>(
                        estimatedIndicesPerSection
                    );

                sectionTriangles[
                    sectionIndex
                ] =
                    indices;
            }

            indices.Add(
                a
            );

            indices.Add(
                b
            );

            indices.Add(
                c
            );
        }

        List<WireframeSectionData> result =
            new List<WireframeSectionData>(
                sectionCount
            );

        for (
            int sectionIndex = 0;
            sectionIndex < sectionCount;
            sectionIndex++
        )
        {
            List<int> indices =
                sectionTriangles[
                    sectionIndex
                ];

            if (
                indices == null
                ||
                indices.Count <= 0
            )
            {
                continue;
            }

            result.Add(
                BuildSection(
                    sourceMeshName,
                    sectionIndex,
                    sourceVertices,
                    sourceClipmapData,
                    indices
                )
            );
        }

        return
            result;
    }

    private static WireframeSectionData BuildSection(
        string sourceMeshName,
        int sectionIndex,
        List<Vector3> sourceVertices,
        List<Vector4> sourceClipmapData,
        List<int> sourceTriangleIndices
    )
    {
        int estimatedVertexCount =
            Mathf.Min(
                sourceVertices.Count,
                sourceTriangleIndices.Count
            );

        Dictionary<int, int> sourceToLocal =
            new Dictionary<int, int>(
                estimatedVertexCount
            );

        List<Vector3> vertices =
            new List<Vector3>(
                estimatedVertexCount
            );

        List<Vector4> clipmapData =
            new List<Vector4>(
                estimatedVertexCount
            );

        List<int> triangleIndices =
            new List<int>(
                sourceTriangleIndices.Count
            );

        HashSet<ulong> uniqueEdges =
            new HashSet<ulong>();

        List<int> lineIndices =
            new List<int>(
                sourceTriangleIndices.Count
            );

        for (
            int index = 0;
            index + 2 < sourceTriangleIndices.Count;
            index += 3
        )
        {
            int a =
                GetOrAddLocalVertex(
                    sourceTriangleIndices[index],
                    sourceVertices,
                    sourceClipmapData,
                    sourceToLocal,
                    vertices,
                    clipmapData
                );

            int b =
                GetOrAddLocalVertex(
                    sourceTriangleIndices[index + 1],
                    sourceVertices,
                    sourceClipmapData,
                    sourceToLocal,
                    vertices,
                    clipmapData
                );

            int c =
                GetOrAddLocalVertex(
                    sourceTriangleIndices[index + 2],
                    sourceVertices,
                    sourceClipmapData,
                    sourceToLocal,
                    vertices,
                    clipmapData
                );

            triangleIndices.Add(
                a
            );

            triangleIndices.Add(
                b
            );

            triangleIndices.Add(
                c
            );

            AddUniqueEdge(
                a,
                b,
                uniqueEdges,
                lineIndices
            );

            AddUniqueEdge(
                b,
                c,
                uniqueEdges,
                lineIndices
            );

            AddUniqueEdge(
                c,
                a,
                uniqueEdges,
                lineIndices
            );
        }

        if (
            triangleIndices.Count <= 0
            ||
            lineIndices.Count <= 0
        )
        {
            throw new InvalidOperationException(
                $"Generated wireframe section {sectionIndex} for " +
                $"'{sourceMeshName}' contains no renderable topology."
            );
        }

        return
            new WireframeSectionData(
                sectionIndex,
                vertices,
                clipmapData,
                triangleIndices,
                lineIndices
            );
    }

    private static int GetOrAddLocalVertex(
        int sourceIndex,
        List<Vector3> sourceVertices,
        List<Vector4> sourceClipmapData,
        Dictionary<int, int> sourceToLocal,
        List<Vector3> vertices,
        List<Vector4> clipmapData
    )
    {
        if (
            sourceToLocal.TryGetValue(
                sourceIndex,
                out int localIndex
            )
        )
        {
            return
                localIndex;
        }

        localIndex =
            vertices.Count;

        sourceToLocal.Add(
            sourceIndex,
            localIndex
        );

        vertices.Add(
            sourceVertices[
                sourceIndex
            ]
        );

        clipmapData.Add(
            sourceClipmapData[
                sourceIndex
            ]
        );

        return
            localIndex;
    }

    private static void AddUniqueEdge(
        int indexA,
        int indexB,
        HashSet<ulong> uniqueEdges,
        List<int> lineIndices
    )
    {
        if (indexA == indexB)
        {
            return;
        }

        int minimumIndex =
            Mathf.Min(
                indexA,
                indexB
            );

        int maximumIndex =
            Mathf.Max(
                indexA,
                indexB
            );

        ulong edgeKey =
            (
                (ulong)(uint)minimumIndex
                <<
                32
            )
            |
            (uint)maximumIndex;

        if (
            !uniqueEdges.Add(
                edgeKey
            )
        )
        {
            return;
        }

        lineIndices.Add(
            minimumIndex
        );

        lineIndices.Add(
            maximumIndex
        );
    }

    // =====================================================
    // SAVE / UPDATE PREVIEW ASSET
    // =====================================================

    private static void SaveOrUpdatePreviewAsset(
        string assetPath,
        string sourceMeshName,
        List<WireframeSectionData> sections
    )
    {
        Mesh mainMesh =
            AssetDatabase.LoadAssetAtPath<Mesh>(
                assetPath
            );

        if (mainMesh == null)
        {
            mainMesh =
                new Mesh();

            WriteSectionMesh(
                mainMesh,
                sourceMeshName,
                sections[0]
            );

            AssetDatabase.CreateAsset(
                mainMesh,
                assetPath
            );

            for (
                int index = 1;
                index < sections.Count;
                index++
            )
            {
                Mesh subMesh =
                    new Mesh();

                WriteSectionMesh(
                    subMesh,
                    sourceMeshName,
                    sections[index]
                );

                AssetDatabase.AddObjectToAsset(
                    subMesh,
                    assetPath
                );
            }

            return;
        }

        UnityEngine.Object[] existingObjects =
            AssetDatabase.LoadAllAssetsAtPath(
                assetPath
            );

        Dictionary<string, Mesh> reusableSubMeshes =
            new Dictionary<string, Mesh>();

        List<Mesh> duplicateOrUnnamedSubMeshes =
            new List<Mesh>();

        foreach (
            UnityEngine.Object existingObject
            in existingObjects
        )
        {
            Mesh existingMesh =
                existingObject as Mesh;

            if (
                existingMesh == null
                ||
                existingMesh == mainMesh
            )
            {
                continue;
            }

            if (
                string.IsNullOrEmpty(
                    existingMesh.name
                )
                ||
                reusableSubMeshes.ContainsKey(
                    existingMesh.name
                )
            )
            {
                duplicateOrUnnamedSubMeshes.Add(
                    existingMesh
                );

                continue;
            }

            reusableSubMeshes.Add(
                existingMesh.name,
                existingMesh
            );
        }

        HashSet<Mesh> usedSubMeshes =
            new HashSet<Mesh>();

        WriteSectionMesh(
            mainMesh,
            sourceMeshName,
            sections[0]
        );

        EditorUtility.SetDirty(
            mainMesh
        );

        for (
            int index = 1;
            index < sections.Count;
            index++
        )
        {
            WireframeSectionData section =
                sections[index];

            string sectionName =
                GetSectionMeshName(
                    sourceMeshName,
                    section.SectionIndex
                );

            Mesh targetMesh;

            if (
                reusableSubMeshes.TryGetValue(
                    sectionName,
                    out Mesh existingMesh
                )
                &&
                existingMesh != null
            )
            {
                targetMesh =
                    existingMesh;

                usedSubMeshes.Add(
                    targetMesh
                );
            }
            else
            {
                targetMesh =
                    new Mesh();

                WriteSectionMesh(
                    targetMesh,
                    sourceMeshName,
                    section
                );

                AssetDatabase.AddObjectToAsset(
                    targetMesh,
                    assetPath
                );

                EditorUtility.SetDirty(
                    targetMesh
                );

                continue;
            }

            WriteSectionMesh(
                targetMesh,
                sourceMeshName,
                section
            );

            EditorUtility.SetDirty(
                targetMesh
            );
        }

        foreach (
            KeyValuePair<string, Mesh> pair
            in reusableSubMeshes
        )
        {
            Mesh mesh =
                pair.Value;

            if (
                mesh != null
                &&
                !usedSubMeshes.Contains(
                    mesh
                )
            )
            {
                UnityEngine.Object.DestroyImmediate(
                    mesh,
                    true
                );
            }
        }

        foreach (
            Mesh mesh
            in duplicateOrUnnamedSubMeshes
        )
        {
            if (mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    mesh,
                    true
                );
            }
        }
    }

    private static void WriteSectionMesh(
        Mesh mesh,
        string sourceMeshName,
        WireframeSectionData section
    )
    {
        mesh.Clear(
            false
        );

        mesh.name =
            GetSectionMeshName(
                sourceMeshName,
                section.SectionIndex
            );

        mesh.indexFormat =
            section.Vertices.Count >
                65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

        mesh.SetVertices(
            section.Vertices
        );

        mesh.SetUVs(
            3,
            section.ClipmapData
        );

        mesh.subMeshCount =
            2;

        mesh.SetIndices(
            section.TriangleIndices,
            MeshTopology.Triangles,
            0,
            false
        );

        mesh.SetIndices(
            section.LineIndices,
            MeshTopology.Lines,
            1,
            false
        );

        mesh.RecalculateBounds();
    }

    private static string GetSectionMeshName(
        string sourceMeshName,
        int sectionIndex
    )
    {
        return
            $"{sourceMeshName}_WireframeSection_{sectionIndex:D2}";
    }

    // =====================================================
    // SECTION POLICY HELPERS
    // =====================================================

    private static int ResolveSectionsPerAxis(
        int triangleCount
    )
    {
        if (triangleCount <= 0)
        {
            return 1;
        }

        int desiredSectionCount =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    triangleCount /
                    (float)TargetTrianglesPerSection
                )
            );

        int sectionsPerAxis =
            Mathf.CeilToInt(
                Mathf.Sqrt(
                    desiredSectionCount
                )
            );

        return
            Mathf.Clamp(
                sectionsPerAxis,
                1,
                MaximumSectionsPerAxis
            );
    }

    private static int ResolveSectionIndex(
        Bounds sourceBounds,
        int sectionsPerAxis,
        Vector3 localPosition
    )
    {
        if (sectionsPerAxis <= 1)
        {
            return 0;
        }

        float normalizedX =
            sourceBounds.size.x > 0.000001f
                ? (
                    localPosition.x -
                    sourceBounds.min.x
                ) /
                sourceBounds.size.x
                : 0f;

        float normalizedZ =
            sourceBounds.size.z > 0.000001f
                ? (
                    localPosition.z -
                    sourceBounds.min.z
                ) /
                sourceBounds.size.z
                : 0f;

        int x =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedX *
                    sectionsPerAxis
                ),
                0,
                sectionsPerAxis - 1
            );

        int z =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedZ *
                    sectionsPerAxis
                ),
                0,
                sectionsPerAxis - 1
            );

        return
            z *
            sectionsPerAxis +
            x;
    }

    private static Bounds CalculateBounds(
        List<Vector3> vertices
    )
    {
        Vector3 minimum =
            vertices[0];

        Vector3 maximum =
            vertices[0];

        for (
            int index = 1;
            index < vertices.Count;
            index++
        )
        {
            minimum =
                Vector3.Min(
                    minimum,
                    vertices[index]
                );

            maximum =
                Vector3.Max(
                    maximum,
                    vertices[index]
                );
        }

        Bounds bounds =
            new Bounds();

        bounds.SetMinMax(
            minimum,
            maximum
        );

        return
            bounds;
    }

    // =====================================================
    // VALIDATION / FOLDERS
    // =====================================================

    private static void ValidateSourceData(
        string sourceMeshName,
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector4> clipmapData
    )
    {
        if (
            string.IsNullOrEmpty(
                sourceMeshName
            )
        )
        {
            throw new ArgumentException(
                "Wireframe preview source mesh name is empty."
            );
        }

        if (
            vertices == null
            ||
            vertices.Count <= 0
        )
        {
            throw new InvalidOperationException(
                $"Wireframe preview source '{sourceMeshName}' has no vertices."
            );
        }

        if (
            triangles == null
            ||
            triangles.Count <= 0
            ||
            triangles.Count % 3 != 0
        )
        {
            throw new InvalidOperationException(
                $"Wireframe preview source '{sourceMeshName}' has invalid triangle data."
            );
        }

        if (
            clipmapData == null
            ||
            clipmapData.Count != vertices.Count
        )
        {
            throw new InvalidOperationException(
                $"Wireframe preview source '{sourceMeshName}' does not contain one TEXCOORD3 value per vertex."
            );
        }
    }

    private static void ValidateTriangleIndices(
        string sourceMeshName,
        int a,
        int b,
        int c,
        int vertexCount
    )
    {
        if (
            a < 0
            ||
            a >= vertexCount
            ||
            b < 0
            ||
            b >= vertexCount
            ||
            c < 0
            ||
            c >= vertexCount
        )
        {
            throw new InvalidOperationException(
                $"Wireframe preview source '{sourceMeshName}' contains an invalid triangle vertex index."
            );
        }
    }

    private static void EnsureFolderExists()
    {
        if (
            !AssetDatabase.IsValidFolder(
                WorldMeshesPaths.GeneratedWireframePreviewMeshes
            )
        )
        {
            AssetDatabase.CreateFolder(
                WorldMeshesPaths.GeneratedMeshes,
                "WireframePreview"
            );
        }
    }

    // =====================================================
    // DATA
    // =====================================================

    private sealed class WireframeSectionData
    {
        public readonly int SectionIndex;

        public readonly List<Vector3> Vertices;

        public readonly List<Vector4> ClipmapData;

        public readonly List<int> TriangleIndices;

        public readonly List<int> LineIndices;

        public WireframeSectionData(
            int sectionIndex,
            List<Vector3> vertices,
            List<Vector4> clipmapData,
            List<int> triangleIndices,
            List<int> lineIndices
        )
        {
            SectionIndex =
                sectionIndex;

            Vertices =
                vertices;

            ClipmapData =
                clipmapData;

            TriangleIndices =
                triangleIndices;

            LineIndices =
                lineIndices;
        }
    }
}
