using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/*
 * Editor-only cache of generated true-wireframe preview section assets.
 *
 * Source registration binds clipmap renderers to already-generated
 * explicit-edge section meshes from the deterministic preview asset owned
 * by each source clipmap mesh. Scene View repaint performs visibility tests
 * and RenderMesh submission only; it never traverses source triangles or
 * constructs preview topology.
 */
public static class TerrainAuthoringWireframeSectionCache
{
    // =====================================================
    // CACHE STATE
    // =====================================================

    private static readonly Dictionary<int, SourceEntry>
        entries =
            new Dictionary<int, SourceEntry>();

    private static MaterialPropertyBlock drawPropertyBlock;

    private static int generatedSectionCount;

    private static int generatedEdgeCount;

    private static string previewAssetStatusLabel =
        "Not Loaded";

    // =====================================================
    // PUBLIC DIAGNOSTICS
    // =====================================================

    public static int SourceRendererCount
    {
        get
        {
            return
                entries.Count;
        }
    }

    public static int GeneratedSectionCount
    {
        get
        {
            return
                generatedSectionCount;
        }
    }

    public static int GeneratedEdgeCount
    {
        get
        {
            return
                generatedEdgeCount;
        }
    }

    public static string PreviewAssetStatusLabel
    {
        get
        {
            return
                previewAssetStatusLabel;
        }
    }

    // =====================================================
    // SOURCE SYNCHRONIZATION
    // =====================================================

    public static bool TryRegisterSource(
        MeshRenderer renderer,
        Mesh sourceMesh,
        Transform clipmapRoot,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            renderer == null
            ||
            sourceMesh == null
        )
        {
            errorMessage =
                "The wireframe preview source renderer or mesh is null.";

            previewAssetStatusLabel =
                "Missing";

            return false;
        }

        int rendererId =
            renderer.GetInstanceID();

        int lodLevel =
            DetermineLODLevel(
                renderer.transform,
                clipmapRoot
            );

        if (
            entries.TryGetValue(
                rendererId,
                out SourceEntry existingEntry
            )
            &&
            existingEntry != null
            &&
            existingEntry.SourceRenderer == renderer
            &&
            existingEntry.SourceMesh == sourceMesh
            &&
            TryValidateSectionsAgainstSource(
                sourceMesh,
                existingEntry.Sections,
                out _
            )
        )
        {
            existingEntry.LODLevel =
                lodLevel;

            previewAssetStatusLabel =
                "Ready";

            return true;
        }

        if (existingEntry != null)
        {
            RemoveEntry(
                rendererId,
                existingEntry
            );
        }

        if (
            !TryLoadGeneratedSections(
                sourceMesh,
                out WireframeSection[] sections,
                out string loadError
            )
        )
        {
            previewAssetStatusLabel =
                "Missing / Regenerate";

            errorMessage =
                loadError;

            return false;
        }

        SourceEntry newEntry =
            new SourceEntry(
                renderer,
                sourceMesh,
                lodLevel,
                sections
            );

        entries.Add(
            rendererId,
            newEntry
        );

        generatedSectionCount +=
            sections.Length;

        generatedEdgeCount +=
            newEntry.EdgeCount;

        previewAssetStatusLabel =
            "Ready";

        return true;
    }

    public static void RemoveStaleSources(
        HashSet<int> seenRendererIds
    )
    {
        if (seenRendererIds == null)
        {
            return;
        }

        List<int> staleIds =
            null;

        foreach (
            KeyValuePair<int, SourceEntry> pair
            in entries
        )
        {
            if (
                !seenRendererIds.Contains(
                    pair.Key
                )
            )
            {
                if (staleIds == null)
                {
                    staleIds =
                        new List<int>();
                }

                staleIds.Add(
                    pair.Key
                );
            }
        }

        if (staleIds == null)
        {
            return;
        }

        foreach (
            int staleId
            in staleIds
        )
        {
            if (
                entries.TryGetValue(
                    staleId,
                    out SourceEntry staleEntry
                )
            )
            {
                RemoveEntry(
                    staleId,
                    staleEntry
                );
            }
        }
    }

    private static bool TryLoadGeneratedSections(
        Mesh sourceMesh,
        out WireframeSection[] sections,
        out string errorMessage
    )
    {
        sections =
            null;

        errorMessage =
            "";

        string assetPath =
            TerrainClipmapWireframePreviewGenerator
                .GetPreviewAssetPath(
                    sourceMesh.name
                );

        if (
            string.IsNullOrEmpty(
                assetPath
            )
        )
        {
            errorMessage =
                $"Could not resolve a generated wireframe preview path for '{sourceMesh.name}'.";

            return false;
        }

        Object[] assets =
            AssetDatabase.LoadAllAssetsAtPath(
                assetPath
            );

        if (
            assets == null
            ||
            assets.Length == 0
        )
        {
            errorMessage =
                "Wireframe preview assets are missing or out of date. " +
                "Regenerate Clipmap Meshes.\n\n" +
                $"Missing preview for:\n{sourceMesh.name}\n\n" +
                $"Expected asset:\n{assetPath}";

            return false;
        }

        List<WireframeSection> loadedSections =
            new List<WireframeSection>();

        foreach (
            Object asset
            in assets
        )
        {
            Mesh mesh =
                asset as Mesh;

            if (mesh == null)
            {
                continue;
            }

            if (
                !TryValidateGeneratedSection(
                    mesh,
                    out int edgeCount
                )
            )
            {
                continue;
            }

            loadedSections.Add(
                new WireframeSection(
                    mesh,
                    edgeCount
                )
            );
        }

        if (loadedSections.Count <= 0)
        {
            errorMessage =
                "Wireframe preview assets are missing or incompatible. " +
                "Regenerate Clipmap Meshes.\n\n" +
                $"Preview asset:\n{assetPath}";

            return false;
        }

        sections =
            loadedSections.ToArray();

        if (
            !TryValidateSectionsAgainstSource(
                sourceMesh,
                sections,
                out string validationError
            )
        )
        {
            sections =
                null;

            errorMessage =
                validationError;

            return false;
        }

        return true;
    }

    private static bool TryValidateSectionsAgainstSource(
        Mesh sourceMesh,
        WireframeSection[] sections,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            sourceMesh == null
            ||
            sections == null
            ||
            sections.Length <= 0
        )
        {
            errorMessage =
                "Wireframe preview bindings are missing or invalid. " +
                "Regenerate Clipmap Meshes.";

            return false;
        }

        ulong sourceTriangleIndexCount =
            CalculateTriangleIndexCount(
                sourceMesh
            );

        ulong previewTriangleIndexCount =
            0UL;

        bool hasPreviewBounds =
            false;

        Bounds previewBounds =
            new Bounds();

        foreach (
            WireframeSection section
            in sections
        )
        {
            if (
                section == null
                ||
                !TryValidateGeneratedSection(
                    section.Mesh,
                    out _
                )
            )
            {
                errorMessage =
                    "Wireframe preview assets are missing or incompatible. " +
                    "Regenerate Clipmap Meshes.\n\n" +
                    $"Source mesh:\n{sourceMesh.name}";

                return false;
            }

            Mesh sectionMesh =
                section.Mesh;

            previewTriangleIndexCount +=
                sectionMesh.GetIndexCount(
                    0
                );

            if (!hasPreviewBounds)
            {
                previewBounds =
                    sectionMesh.bounds;

                hasPreviewBounds =
                    true;
            }
            else
            {
                previewBounds.Encapsulate(
                    sectionMesh.bounds.min
                );

                previewBounds.Encapsulate(
                    sectionMesh.bounds.max
                );
            }
        }

        if (
            sourceTriangleIndexCount <= 0UL
            ||
            previewTriangleIndexCount !=
                sourceTriangleIndexCount
        )
        {
            errorMessage =
                "Wireframe preview assets are out of date. " +
                "Regenerate Clipmap Meshes.\n\n" +
                $"Source mesh: {sourceMesh.name}\n" +
                $"Source triangle indices: {sourceTriangleIndexCount}\n" +
                $"Preview triangle indices: {previewTriangleIndexCount}";

            return false;
        }

        if (
            !hasPreviewBounds
            ||
            !BoundsApproximatelyMatch(
                sourceMesh.bounds,
                previewBounds
            )
        )
        {
            errorMessage =
                "Wireframe preview assets are out of date. " +
                "Regenerate Clipmap Meshes.\n\n" +
                $"Source mesh:\n{sourceMesh.name}";

            return false;
        }

        return true;
    }

    private static ulong CalculateTriangleIndexCount(
        Mesh mesh
    )
    {
        if (mesh == null)
        {
            return 0UL;
        }

        ulong indexCount =
            0UL;

        for (
            int subMeshIndex = 0;
            subMeshIndex < mesh.subMeshCount;
            subMeshIndex++
        )
        {
            if (
                mesh.GetTopology(
                    subMeshIndex
                ) !=
                MeshTopology.Triangles
            )
            {
                continue;
            }

            indexCount +=
                mesh.GetIndexCount(
                    subMeshIndex
                );
        }

        return
            indexCount;
    }

    private static bool BoundsApproximatelyMatch(
        Bounds sourceBounds,
        Bounds previewBounds
    )
    {
        float maximumSize =
            Mathf.Max(
                1f,
                Mathf.Max(
                    sourceBounds.size.x,
                    Mathf.Max(
                        sourceBounds.size.y,
                        sourceBounds.size.z
                    )
                )
            );

        float tolerance =
            maximumSize *
            0.0001f;

        return
            VectorApproximatelyMatches(
                sourceBounds.min,
                previewBounds.min,
                tolerance
            )
            &&
            VectorApproximatelyMatches(
                sourceBounds.max,
                previewBounds.max,
                tolerance
            );
    }

    private static bool VectorApproximatelyMatches(
        Vector3 a,
        Vector3 b,
        float tolerance
    )
    {
        return
            Mathf.Abs(a.x - b.x) <= tolerance
            &&
            Mathf.Abs(a.y - b.y) <= tolerance
            &&
            Mathf.Abs(a.z - b.z) <= tolerance;
    }

    private static bool TryValidateGeneratedSection(
        Mesh mesh,
        out int edgeCount
    )
    {
        edgeCount =
            0;

        if (
            mesh == null
            ||
            mesh.subMeshCount < 2
            ||
            mesh.vertexCount <= 0
            ||
            mesh.GetTopology(0) != MeshTopology.Triangles
            ||
            mesh.GetTopology(1) != MeshTopology.Lines
            ||
            mesh.GetIndexCount(0) <= 0
            ||
            mesh.GetIndexCount(1) <= 0
            ||
            !mesh.HasVertexAttribute(
                VertexAttribute.TexCoord3
            )
        )
        {
            return false;
        }

        ulong lineIndexCount =
            mesh.GetIndexCount(
                1
            );

        edgeCount =
            lineIndexCount /
                2UL >
                int.MaxValue
                ? int.MaxValue
                : (int)(
                    lineIndexCount /
                    2UL
                );

        return
            edgeCount > 0;
    }

    // =====================================================
    // SCENE VIEW RENDERING
    // =====================================================

    public static void RenderSceneView(
        Camera camera,
        Material lineMaterial,
        Material depthMaterial,
        bool wireframeOnly,
        Color color,
        float opacity
    )
    {
        if (
            camera == null
            ||
            lineMaterial == null
            ||
            depthMaterial == null
        )
        {
            return;
        }

        TerrainAuthoringWireframeCulling
            .BeginSceneViewRepaint(
                camera
            );

        EvaluateVisibleSections();

        EnsurePropertyBlock();

        if (wireframeOnly)
        {
            DrawVisibleSections(
                camera,
                depthMaterial,
                0,
                color,
                opacity,
                false
            );
        }

        DrawVisibleSections(
            camera,
            lineMaterial,
            1,
            color,
            opacity,
            true
        );
    }

    private static void EvaluateVisibleSections()
    {
        foreach (
            SourceEntry entry
            in entries.Values
        )
        {
            if (
                entry == null
                ||
                entry.SourceRenderer == null
                ||
                entry.SourceMesh == null
            )
            {
                continue;
            }

            MeshRenderer sourceRenderer =
                entry.SourceRenderer;

            if (
                !sourceRenderer.enabled
                ||
                !sourceRenderer.gameObject.activeInHierarchy
            )
            {
                ClearVisibleFlags(
                    entry
                );

                continue;
            }

            MeshFilter meshFilter =
                entry.SourceMeshFilter;

            if (
                meshFilter == null
                ||
                meshFilter.sharedMesh == null
                ||
                meshFilter.sharedMesh != entry.SourceMesh
            )
            {
                TerrainAuthoringWireframeRenderer
                    .RequestReapply();

                ClearVisibleFlags(
                    entry
                );

                continue;
            }

            Matrix4x4 matrix =
                sourceRenderer.localToWorldMatrix;

            foreach (
                WireframeSection section
                in entry.Sections
            )
            {
                section.VisibleThisRepaint =
                    false;

                Bounds localBounds =
                    CalculateConservativeLocalBounds(
                        entry,
                        section.Mesh.bounds
                    );

                Bounds worldBounds =
                    TransformBounds(
                        localBounds,
                        matrix
                    );

                TerrainAuthoringWireframeSectionVisibility visibility =
                    TerrainAuthoringWireframeCulling
                        .EvaluateSection(
                            entry.LODLevel,
                            worldBounds
                        );

                if (
                    visibility !=
                    TerrainAuthoringWireframeSectionVisibility.Visible
                )
                {
                    continue;
                }

                section.VisibleThisRepaint =
                    true;

                section.WorldBoundsThisRepaint =
                    worldBounds;
            }
        }
    }

    private static void DrawVisibleSections(
        Camera camera,
        Material material,
        int subMeshIndex,
        Color color,
        float opacity,
        bool recordRendered
    )
    {
        foreach (
            SourceEntry entry
            in entries.Values
        )
        {
            if (
                entry == null
                ||
                entry.SourceRenderer == null
            )
            {
                continue;
            }

            MeshRenderer sourceRenderer =
                entry.SourceRenderer;

            if (
                !sourceRenderer.enabled
                ||
                !sourceRenderer.gameObject.activeInHierarchy
            )
            {
                continue;
            }

            bool hasVisibleSection =
                false;

            foreach (
                WireframeSection section
                in entry.Sections
            )
            {
                if (section.VisibleThisRepaint)
                {
                    hasVisibleSection =
                        true;

                    break;
                }
            }

            if (!hasVisibleSection)
            {
                continue;
            }

            sourceRenderer.GetPropertyBlock(
                drawPropertyBlock
            );

            drawPropertyBlock.SetColor(
                TerrainAuthoringWireframeRenderer
                    .WireframeColorPropertyId,
                color
            );

            drawPropertyBlock.SetFloat(
                TerrainAuthoringWireframeRenderer
                    .WireframeOpacityPropertyId,
                opacity
            );

            Matrix4x4 matrix =
                sourceRenderer.localToWorldMatrix;

            int layer =
                sourceRenderer.gameObject.layer;

            foreach (
                WireframeSection section
                in entry.Sections
            )
            {
                if (!section.VisibleThisRepaint)
                {
                    continue;
                }

                RenderParams renderParams =
                    CreateRenderParams(
                        material,
                        camera,
                        layer,
                        drawPropertyBlock,
                        section.WorldBoundsThisRepaint
                    );

                Graphics.RenderMesh(
                    renderParams,
                    section.Mesh,
                    subMeshIndex,
                    matrix
                );

                if (recordRendered)
                {
                    TerrainAuthoringWireframeCulling
                        .RecordRendered(
                            section.EdgeCount
                        );
                }
            }
        }
    }

    private static RenderParams CreateRenderParams(
        Material material,
        Camera camera,
        int layer,
        MaterialPropertyBlock propertyBlock,
        Bounds worldBounds
    )
    {
        RenderParams renderParams =
            new RenderParams(
                material
            );

        renderParams.camera =
            camera;

        renderParams.layer =
            layer;

        renderParams.matProps =
            propertyBlock;

        /*
         * Generated section assets keep their exact undisplaced local
         * bounds. Supply the current conservative displaced world bounds
         * here so Unity's RenderMesh culling matches the source renderer
         * without mutating or dirtying the persistent preview asset.
         */
        renderParams.worldBounds =
            worldBounds;

        renderParams.shadowCastingMode =
            ShadowCastingMode.Off;

        renderParams.receiveShadows =
            false;

        renderParams.lightProbeUsage =
            LightProbeUsage.Off;

        renderParams.reflectionProbeUsage =
            ReflectionProbeUsage.Off;

        return
            renderParams;
    }

    // =====================================================
    // BOUNDS
    // =====================================================

    private static Bounds CalculateConservativeLocalBounds(
        SourceEntry entry,
        Bounds sectionBounds
    )
    {
        Bounds sourceRendererBounds =
            entry.SourceRenderer.localBounds;

        Bounds sourceMeshBounds =
            entry.SourceMesh.bounds;

        Vector3 minimum =
            sectionBounds.min;

        Vector3 maximum =
            sectionBounds.max;

        minimum.y =
            sourceRendererBounds.min.y;

        maximum.y =
            sourceRendererBounds.max.y;

        float extraX =
            Mathf.Max(
                0f,
                sourceRendererBounds.extents.x -
                sourceMeshBounds.extents.x
            );

        float extraZ =
            Mathf.Max(
                0f,
                sourceRendererBounds.extents.z -
                sourceMeshBounds.extents.z
            );

        minimum.x -=
            extraX;

        maximum.x +=
            extraX;

        minimum.z -=
            extraZ;

        maximum.z +=
            extraZ;

        Bounds bounds =
            new Bounds();

        bounds.SetMinMax(
            minimum,
            maximum
        );

        return
            bounds;
    }

    private static Bounds TransformBounds(
        Bounds localBounds,
        Matrix4x4 localToWorld
    )
    {
        Vector3 worldCenter =
            localToWorld.MultiplyPoint3x4(
                localBounds.center
            );

        Vector3 localExtents =
            localBounds.extents;

        Vector3 axisX =
            localToWorld.MultiplyVector(
                new Vector3(
                    localExtents.x,
                    0f,
                    0f
                )
            );

        Vector3 axisY =
            localToWorld.MultiplyVector(
                new Vector3(
                    0f,
                    localExtents.y,
                    0f
                )
            );

        Vector3 axisZ =
            localToWorld.MultiplyVector(
                new Vector3(
                    0f,
                    0f,
                    localExtents.z
                )
            );

        Vector3 worldExtents =
            new Vector3(
                Mathf.Abs(axisX.x) +
                    Mathf.Abs(axisY.x) +
                    Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) +
                    Mathf.Abs(axisY.y) +
                    Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) +
                    Mathf.Abs(axisY.z) +
                    Mathf.Abs(axisZ.z)
            );

        return
            new Bounds(
                worldCenter,
                worldExtents *
                    2f
            );
    }

    // =====================================================
    // BINDING CACHE
    // =====================================================

    public static void ClearBindings()
    {
        entries.Clear();

        generatedSectionCount =
            0;

        generatedEdgeCount =
            0;

        previewAssetStatusLabel =
            "Not Loaded";

        TerrainAuthoringWireframeCulling
            .ResetRenderedDiagnostics();
    }

    private static void RemoveEntry(
        int rendererId,
        SourceEntry entry
    )
    {
        if (entry != null)
        {
            generatedSectionCount =
                Mathf.Max(
                    0,
                    generatedSectionCount -
                    entry.Sections.Length
                );

            generatedEdgeCount =
                Mathf.Max(
                    0,
                    generatedEdgeCount -
                    entry.EdgeCount
                );
        }

        entries.Remove(
            rendererId
        );
    }

    // =====================================================
    // LOD METADATA
    // =====================================================

    private static int DetermineLODLevel(
        Transform rendererTransform,
        Transform clipmapRoot
    )
    {
        Transform current =
            rendererTransform;

        while (
            current != null
            &&
            current != clipmapRoot
        )
        {
            if (
                current.name ==
                "Center_LOD0"
            )
            {
                return 0;
            }

            if (
                TryParseLODGroupName(
                    current.name,
                    out int level
                )
            )
            {
                return
                    Mathf.Max(
                        0,
                        level
                    );
            }

            current =
                current.parent;
        }

        return 0;
    }

    private static bool TryParseLODGroupName(
        string objectName,
        out int level
    )
    {
        level =
            0;

        if (
            string.IsNullOrEmpty(
                objectName
            )
            ||
            !objectName.StartsWith(
                "LOD"
            )
            ||
            objectName.Length <= 3
        )
        {
            return false;
        }

        return
            int.TryParse(
                objectName.Substring(
                    3
                ),
                out level
            );
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static void ClearVisibleFlags(
        SourceEntry entry
    )
    {
        foreach (
            WireframeSection section
            in entry.Sections
        )
        {
            section.VisibleThisRepaint =
                false;
        }
    }

    private static void EnsurePropertyBlock()
    {
        if (drawPropertyBlock == null)
        {
            drawPropertyBlock =
                new MaterialPropertyBlock();
        }
    }

    // =====================================================
    // DATA TYPES
    // =====================================================

    private sealed class SourceEntry
    {
        public readonly MeshRenderer SourceRenderer;

        public readonly Mesh SourceMesh;

        public readonly MeshFilter SourceMeshFilter;

        public readonly WireframeSection[] Sections;

        public readonly int EdgeCount;

        public int LODLevel;

        public SourceEntry(
            MeshRenderer sourceRenderer,
            Mesh sourceMesh,
            int lodLevel,
            WireframeSection[] sections
        )
        {
            SourceRenderer =
                sourceRenderer;

            SourceMesh =
                sourceMesh;

            SourceMeshFilter =
                sourceRenderer
                    .GetComponent<MeshFilter>();

            LODLevel =
                lodLevel;

            Sections =
                sections;

            int totalEdges =
                0;

            foreach (
                WireframeSection section
                in sections
            )
            {
                totalEdges +=
                    section.EdgeCount;
            }

            EdgeCount =
                totalEdges;
        }
    }

    private sealed class WireframeSection
    {
        public readonly Mesh Mesh;

        public readonly int EdgeCount;

        public bool VisibleThisRepaint;

        public Bounds WorldBoundsThisRepaint;

        public WireframeSection(
            Mesh mesh,
            int edgeCount
        )
        {
            Mesh =
                mesh;

            EdgeCount =
                edgeCount;
        }
    }
}
