using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

/*
 * Editor-only spatial cache for true displaced wireframe proxies.
 *
 * Source registration creates only cheap section descriptors. Source
 * vertices/UV3/triangles are copied only after an unbuilt section becomes
 * potentially visible, and triangle-to-section indexing is processed in
 * bounded editor-main-thread chunks. Package 3 builds each cached section
 * as one barycentric triangle mesh, eliminating explicit unique-edge
 * HashSets, local vertex-remap dictionaries, and MeshTopology.Lines.
 */
public static class TerrainAuthoringWireframeSectionCache
{
    // =====================================================
    // SECTION POLICY
    // =====================================================

    private const int TargetTrianglesPerSection =
        20000;

    private const int MaximumSectionsPerAxis =
        8;

    private const int TrianglesPerPreparationStep =
        32768;

    // =====================================================
    // CACHE STATE
    // =====================================================

    private static readonly Dictionary<int, SourceEntry>
        entries =
            new Dictionary<int, SourceEntry>();

    private static readonly Queue<SectionBuildRequest>
        pendingBuilds =
            new Queue<SectionBuildRequest>();

    private static SectionBuildRequest activeBuildRequest;

    private static MaterialPropertyBlock drawPropertyBlock;

    private static bool buildPumpScheduled;

    private static int totalSectionDescriptorCount;

    private static int knownEmptySectionCount;

    private static int builtSectionCount;

    private static int cachedEdgeCount;

    private static int pendingBuildCount;

    // =====================================================
    // PACKAGE 3 PROFILING
    // =====================================================

    private static readonly ProfilerMarker SourceReadProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.SourceRead"
        );

    private static readonly ProfilerMarker SourceIndexProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.SourceIndex"
        );

    private static readonly ProfilerMarker SectionBuildProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.SectionBuild"
        );

    private static readonly ProfilerMarker VertexPreparationProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.BarycentricVertexPreparation"
        );

    private static readonly ProfilerMarker MeshUploadProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.MeshUpload"
        );

    private static readonly ProfilerMarker ProxyMeshCreationProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.ProxyMeshCreation"
        );

    private static readonly ProfilerMarker VertexUploadProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.VertexUVUpload"
        );

    private static readonly ProfilerMarker TriangleUploadProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.TriangleIndexUpload"
        );

    private static readonly ProfilerMarker DepthSubmissionProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.DepthSubmission"
        );

    private static readonly ProfilerMarker WireSubmissionProfilerMarker =
        new ProfilerMarker(
            "WorldMeshes.Wireframe.WireSubmission"
        );

    private static double lastSourcePreparationMilliseconds;

    private static double lastSectionBuildMilliseconds;

    private static double totalSectionBuildMilliseconds;

    private static int profiledSectionBuildCount;

    private static double lastVertexPreparationMilliseconds;

    private static double lastMeshUploadMilliseconds;

    private static double lastDepthSubmissionMilliseconds;

    private static double lastWireSubmissionMilliseconds;

    private static bool enableLatencyPending;

    private static double enableLatencyStartSeconds;

    private static double firstVisibleLatencyMilliseconds =
        -1.0;

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

    public static int TotalSectionDescriptorCount
    {
        get
        {
            return
                totalSectionDescriptorCount;
        }
    }

    public static int BuiltSectionCount
    {
        get
        {
            return
                builtSectionCount;
        }
    }

    public static int UnbuiltSectionCount
    {
        get
        {
            return
                Mathf.Max(
                    0,
                    totalSectionDescriptorCount -
                    knownEmptySectionCount -
                    builtSectionCount
                );
        }
    }

    public static int PendingBuildCount
    {
        get
        {
            return
                pendingBuildCount;
        }
    }

    public static int CachedEdgeCount
    {
        get
        {
            return
                cachedEdgeCount;
        }
    }

    public static string ActiveRepresentationLabel
    {
        get
        {
            return
                "Barycentric Triangles";
        }
    }

    public static double LastSourcePreparationMilliseconds
    {
        get
        {
            return
                lastSourcePreparationMilliseconds;
        }
    }

    public static double LastSectionBuildMilliseconds
    {
        get
        {
            return
                lastSectionBuildMilliseconds;
        }
    }

    public static double AverageSectionBuildMilliseconds
    {
        get
        {
            return
                profiledSectionBuildCount <= 0
                    ? 0.0
                    : totalSectionBuildMilliseconds /
                        profiledSectionBuildCount;
        }
    }

    public static double LastVertexPreparationMilliseconds
    {
        get
        {
            return
                lastVertexPreparationMilliseconds;
        }
    }

    public static double LastMeshUploadMilliseconds
    {
        get
        {
            return
                lastMeshUploadMilliseconds;
        }
    }

    public static double LastDepthSubmissionMilliseconds
    {
        get
        {
            return
                lastDepthSubmissionMilliseconds;
        }
    }

    public static double LastWireSubmissionMilliseconds
    {
        get
        {
            return
                lastWireSubmissionMilliseconds;
        }
    }

    public static double FirstVisibleLatencyMilliseconds
    {
        get
        {
            return
                firstVisibleLatencyMilliseconds;
        }
    }

    public static void BeginEnableMeasurement()
    {
        enableLatencyPending =
            true;

        enableLatencyStartSeconds =
            EditorApplication.timeSinceStartup;

        firstVisibleLatencyMilliseconds =
            -1.0;

        lastSourcePreparationMilliseconds =
            0.0;

        lastSectionBuildMilliseconds =
            0.0;

        totalSectionBuildMilliseconds =
            0.0;

        profiledSectionBuildCount =
            0;

        lastVertexPreparationMilliseconds =
            0.0;

        lastMeshUploadMilliseconds =
            0.0;

        lastDepthSubmissionMilliseconds =
            0.0;

        lastWireSubmissionMilliseconds =
            0.0;
    }

    public static void CancelEnableMeasurement()
    {
        enableLatencyPending =
            false;
    }

    // =====================================================
    // SOURCE SYNCHRONIZATION
    // =====================================================

    public static void RegisterSource(
        MeshRenderer renderer,
        Mesh sourceMesh,
        Transform clipmapRoot
    )
    {
        if (
            renderer == null
            ||
            sourceMesh == null
        )
        {
            return;
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
        )
        {
            existingEntry.LODLevel =
                lodLevel;

            return;
        }

        if (existingEntry != null)
        {
            DestroyEntry(
                existingEntry
            );

            entries.Remove(
                rendererId
            );
        }

        SourceEntry newEntry =
            CreateSourceEntry(
                renderer,
                sourceMesh,
                lodLevel
            );

        entries.Add(
            rendererId,
            newEntry
        );

        totalSectionDescriptorCount +=
            newEntry.Sections.Length;
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
                DestroyEntry(
                    staleEntry
                );
            }

            entries.Remove(
                staleId
            );
        }
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

        EvaluateVisibleSectionsAndQueueBuilds();

        EnsurePropertyBlock();

        /*
         * Wireframe Only submits all currently visible depth sections
         * before any visible line section. This preserves the existing
         * hidden-edge occlusion model across section boundaries.
         */
        if (wireframeOnly)
        {
            double depthStartSeconds =
                EditorApplication.timeSinceStartup;

            using (DepthSubmissionProfilerMarker.Auto())
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

            lastDepthSubmissionMilliseconds =
                ElapsedMilliseconds(
                    depthStartSeconds
                );
        }
        else
        {
            lastDepthSubmissionMilliseconds =
                0.0;
        }

        double wireStartSeconds =
            EditorApplication.timeSinceStartup;

        using (WireSubmissionProfilerMarker.Auto())
        {
            DrawVisibleSections(
                camera,
                lineMaterial,
                0,
                color,
                opacity,
                true
            );
        }

        lastWireSubmissionMilliseconds =
            ElapsedMilliseconds(
                wireStartSeconds
            );
    }

    private static void EvaluateVisibleSectionsAndQueueBuilds()
    {
        foreach (
            SourceEntry entry
            in entries.Values
        )
        {
            if (
                entry == null
                ||
                !entry.IsAlive
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

                if (
                    section.KnownEmpty
                    ||
                    section.BuildFailed
                )
                {
                    continue;
                }

                Bounds localBounds =
                    CalculateConservativeLocalBounds(
                        entry,
                        section
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

                if (section.ProxyMesh == null)
                {
                    QueueBuild(
                        entry,
                        section
                    );

                    continue;
                }

                ApplyProxyBoundsIfNeeded(
                    section,
                    localBounds
                );
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
                !entry.IsAlive
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

            bool hasVisibleBuiltSection =
                false;

            foreach (
                WireframeSection section
                in entry.Sections
            )
            {
                if (
                    section.VisibleThisRepaint
                    &&
                    section.ProxyMesh != null
                )
                {
                    hasVisibleBuiltSection =
                        true;

                    break;
                }
            }

            if (!hasVisibleBuiltSection)
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
                if (
                    !section.VisibleThisRepaint
                    ||
                    section.ProxyMesh == null
                )
                {
                    continue;
                }

                RenderParams renderParams =
                    CreateRenderParams(
                        material,
                        camera,
                        layer,
                        drawPropertyBlock
                    );

                Graphics.RenderMesh(
                    renderParams,
                    section.ProxyMesh,
                    subMeshIndex,
                    matrix
                );

                if (recordRendered)
                {
                    TerrainAuthoringWireframeCulling
                        .RecordRendered(
                            section.EdgeCount
                        );

                    CompleteEnableMeasurementIfNeeded();
                }
            }
        }
    }

    private static RenderParams CreateRenderParams(
        Material material,
        Camera camera,
        int layer,
        MaterialPropertyBlock propertyBlock
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
    // LAZY BUILD QUEUE
    // =====================================================

    private static void QueueBuild(
        SourceEntry entry,
        WireframeSection section
    )
    {
        if (
            entry == null
            ||
            section == null
            ||
            !entry.IsAlive
            ||
            section.ProxyMesh != null
            ||
            section.KnownEmpty
            ||
            section.BuildFailed
            ||
            section.BuildQueued
        )
        {
            return;
        }

        section.BuildQueued =
            true;

        pendingBuildCount++;

        pendingBuilds.Enqueue(
            new SectionBuildRequest(
                entry,
                section
            )
        );

        ScheduleBuildPump();
    }

    private static void ScheduleBuildPump()
    {
        if (buildPumpScheduled)
        {
            return;
        }

        buildPumpScheduled =
            true;

        EditorApplication.delayCall +=
            ProcessLazyBuildWork;
    }

    private static void ProcessLazyBuildWork()
    {
        buildPumpScheduled =
            false;

        if (
            EditorApplication.isCompiling
            ||
            EditorApplication.isUpdating
        )
        {
            ScheduleBuildPumpIfNeeded();

            return;
        }

        if (
            !TerrainAuthoringWireframeRenderer.WireframeEnabled
            ||
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
        )
        {
            return;
        }

        while (true)
        {
            if (
                activeBuildRequest == null
                &&
                !TryDequeueNextRequest(
                    out activeBuildRequest
                )
            )
            {
                return;
            }

            SourceEntry entry =
                activeBuildRequest.Entry;

            WireframeSection section =
                activeBuildRequest.Section;

            if (
                !IsRequestValid(
                    entry,
                    section
                )
            )
            {
                FinishQueuedSection(
                    section
                );

                activeBuildRequest =
                    null;

                continue;
            }

            if (
                entry.LODLevel >
                TerrainAuthoringWireframeCulling.MaximumLOD
            )
            {
                FinishQueuedSection(
                    section
                );

                activeBuildRequest =
                    null;

                continue;
            }

            Bounds worldBounds =
                TransformBounds(
                    CalculateConservativeLocalBounds(
                        entry,
                        section
                    ),
                    entry.SourceRenderer.localToWorldMatrix
                );

            if (
                !TerrainAuthoringWireframeCulling
                    .IsPotentiallyVisible(
                        entry.LODLevel,
                        worldBounds
                    )
            )
            {
                FinishQueuedSection(
                    section
                );

                activeBuildRequest =
                    null;

                continue;
            }

            if (
                entry.PreparationState ==
                    SourcePreparationState.Unprepared
            )
            {
                if (
                    !TryBeginSourcePreparation(
                        entry,
                        out string preparationError
                    )
                )
                {
                    FailSectionBuild(
                        section,
                        preparationError
                    );

                    activeBuildRequest =
                        null;

                    return;
                }
            }

            if (
                entry.PreparationState ==
                    SourcePreparationState.Indexing
            )
            {
                ProcessSourcePreparationChunk(
                    entry
                );

                if (
                    entry.PreparationState ==
                    SourcePreparationState.Indexing
                )
                {
                    ScheduleBuildPump();

                    return;
                }
            }

            if (
                entry.PreparationState ==
                    SourcePreparationState.Failed
            )
            {
                FailSectionBuild(
                    section,
                    "Lazy wireframe source preparation failed."
                );

                activeBuildRequest =
                    null;

                return;
            }

            if (
                !IsRequestValid(
                    entry,
                    section
                )
            )
            {
                FinishQueuedSection(
                    section
                );

                activeBuildRequest =
                    null;

                continue;
            }

            if (section.KnownEmpty)
            {
                FinishQueuedSection(
                    section
                );

                activeBuildRequest =
                    null;

                continue;
            }

            bool sectionBuildSucceeded;
            string buildError;

            using (SectionBuildProfilerMarker.Auto())
            {
                sectionBuildSucceeded =
                    TryBuildSectionProxy(
                        entry,
                        section,
                        out buildError
                    );
            }

            if (!sectionBuildSucceeded)
            {
                FailSectionBuild(
                    section,
                    buildError
                );

                activeBuildRequest =
                    null;

                return;
            }

            FinishQueuedSection(
                section
            );

            activeBuildRequest =
                null;

            ReleaseSourceDataIfFullyBuilt(
                entry
            );

            TerrainAuthoringWireframeRenderer
                .RequestRepaint();

            ScheduleBuildPumpIfNeeded();

            return;
        }
    }

    private static bool TryDequeueNextRequest(
        out SectionBuildRequest request
    )
    {
        request =
            null;

        while (
            pendingBuilds.Count > 0
        )
        {
            SectionBuildRequest candidate =
                pendingBuilds.Dequeue();

            if (candidate == null)
            {
                continue;
            }

            if (
                !IsRequestValid(
                    candidate.Entry,
                    candidate.Section
                )
            )
            {
                FinishQueuedSection(
                    candidate.Section
                );

                continue;
            }

            request =
                candidate;

            return true;
        }

        return false;
    }

    private static bool IsRequestValid(
        SourceEntry entry,
        WireframeSection section
    )
    {
        return
            entry != null
            &&
            entry.IsAlive
            &&
            entry.SourceRenderer != null
            &&
            entry.SourceMesh != null
            &&
            section != null
            &&
            section.BuildQueued
            &&
            section.ProxyMesh == null
            &&
            !section.KnownEmpty
            &&
            !section.BuildFailed;
    }

    private static void FinishQueuedSection(
        WireframeSection section
    )
    {
        if (
            section == null
            ||
            !section.BuildQueued
        )
        {
            return;
        }

        section.BuildQueued =
            false;

        pendingBuildCount =
            Mathf.Max(
                0,
                pendingBuildCount - 1
            );
    }

    private static void FailSectionBuild(
        WireframeSection section,
        string errorMessage
    )
    {
        if (section != null)
        {
            section.BuildFailed =
                true;

            FinishQueuedSection(
                section
            );
        }

        TerrainAuthoringWireframeRenderer
            .ReportLazyBuildError(
                string.IsNullOrEmpty(
                    errorMessage
                )
                    ? "Could not build a lazy wireframe section."
                    : errorMessage
            );
    }

    private static void ScheduleBuildPumpIfNeeded()
    {
        if (
            pendingBuildCount > 0
            ||
            activeBuildRequest != null
        )
        {
            ScheduleBuildPump();
        }
    }

    // =====================================================
    // SOURCE PREPARATION
    // =====================================================

    private static bool TryBeginSourcePreparation(
        SourceEntry entry,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        double preparationStartSeconds =
            EditorApplication.timeSinceStartup;

        Mesh sourceMesh =
            entry.SourceMesh;

        if (!sourceMesh.isReadable)
        {
            errorMessage =
                $"Source clipmap mesh '{sourceMesh.name}' is not readable in the Editor.";

            return false;
        }

        List<Vector3> vertices =
            new List<Vector3>();

        using (SourceReadProfilerMarker.Auto())
        {
            sourceMesh.GetVertices(
                vertices
            );
        }

        if (vertices.Count <= 0)
        {
            errorMessage =
                $"Source clipmap mesh '{sourceMesh.name}' has no vertices.";

            return false;
        }

        List<Vector4> clipmapData =
            new List<Vector4>();

        using (SourceReadProfilerMarker.Auto())
        {
            sourceMesh.GetUVs(
                3,
                clipmapData
            );
        }

        if (
            clipmapData.Count !=
            vertices.Count
        )
        {
            errorMessage =
                $"Source clipmap mesh '{sourceMesh.name}' does not contain one UV3/TEXCOORD3 clipmap-data value per vertex.\n\n" +
                $"Vertices: {vertices.Count}\n" +
                $"Clipmap Data: {clipmapData.Count}";

            return false;
        }

        int totalTriangleIndexCount =
            CalculateTriangleIndexCount(
                sourceMesh
            );

        if (totalTriangleIndexCount <= 0)
        {
            errorMessage =
                $"Source clipmap mesh '{sourceMesh.name}' contains no triangle topology suitable for displaced wireframe generation.";

            return false;
        }

        List<int> sourceTriangleIndices =
            new List<int>(
                totalTriangleIndexCount
            );

        using (SourceReadProfilerMarker.Auto())
        {
            for (
                int subMeshIndex = 0;
                subMeshIndex < sourceMesh.subMeshCount;
                subMeshIndex++
            )
            {
                if (
                    sourceMesh.GetTopology(
                        subMeshIndex
                    ) !=
                    MeshTopology.Triangles
                )
                {
                    continue;
                }

                int[] subMeshIndices =
                    sourceMesh.GetIndices(
                        subMeshIndex
                    );

                sourceTriangleIndices.AddRange(
                    subMeshIndices
                );
            }
        }

        entry.SourceVertices =
            vertices;

        entry.SourceClipmapData =
            clipmapData;

        entry.SourceTriangleIndices =
            sourceTriangleIndices;

        entry.SectionTriangleIndices =
            new List<int>[
                entry.Sections.Length
            ];

        entry.PreparationCursor =
            0;

        entry.PreparationState =
            SourcePreparationState.Indexing;

        entry.SourcePreparationCpuMilliseconds =
            ElapsedMilliseconds(
                preparationStartSeconds
            );

        return true;
    }

    private static void ProcessSourcePreparationChunk(
        SourceEntry entry
    )
    {
        List<int> sourceTriangleIndices =
            entry.SourceTriangleIndices;

        List<Vector3> vertices =
            entry.SourceVertices;

        if (
            sourceTriangleIndices == null
            ||
            vertices == null
        )
        {
            entry.PreparationState =
                SourcePreparationState.Failed;

            TerrainAuthoringWireframeRenderer
                .ReportLazyBuildError(
                    "Lazy wireframe source preparation lost its temporary source data."
                );

            return;
        }

        double indexingStartSeconds =
            EditorApplication.timeSinceStartup;

        int maximumIndex =
            Mathf.Min(
                sourceTriangleIndices.Count,
                entry.PreparationCursor +
                    TrianglesPerPreparationStep *
                    3
            );

        int estimatedIndicesPerSection =
            Mathf.Max(
                96,
                sourceTriangleIndices.Count /
                    Mathf.Max(
                        1,
                        entry.Sections.Length
                    )
            );

        SourceIndexProfilerMarker.Begin();

        for (
            int index = entry.PreparationCursor;
            index + 2 < maximumIndex;
            index += 3
        )
        {
            int a =
                sourceTriangleIndices[index];

            int b =
                sourceTriangleIndices[index + 1];

            int c =
                sourceTriangleIndices[index + 2];

            if (
                a < 0
                ||
                a >= vertices.Count
                ||
                b < 0
                ||
                b >= vertices.Count
                ||
                c < 0
                ||
                c >= vertices.Count
            )
            {
                continue;
            }

            Vector3 positionA =
                vertices[a];

            Vector3 positionB =
                vertices[b];

            Vector3 positionC =
                vertices[c];

            Vector3 centroid =
                (
                    positionA +
                    positionB +
                    positionC
                ) /
                3f;

            int sectionIndex =
                ResolveSectionIndex(
                    entry,
                    centroid
                );

            List<int> sectionIndices =
                entry.SectionTriangleIndices[
                    sectionIndex
                ];

            if (sectionIndices == null)
            {
                sectionIndices =
                    new List<int>(
                        estimatedIndicesPerSection
                    );

                entry.SectionTriangleIndices[
                    sectionIndex
                ] =
                    sectionIndices;
            }

            sectionIndices.Add(
                a
            );

            sectionIndices.Add(
                b
            );

            sectionIndices.Add(
                c
            );

            WireframeSection section =
                entry.Sections[
                    sectionIndex
                ];

            section.EncapsulateGeometry(
                positionA
            );

            section.EncapsulateGeometry(
                positionB
            );

            section.EncapsulateGeometry(
                positionC
            );
        }

        SourceIndexProfilerMarker.End();

        entry.SourcePreparationCpuMilliseconds +=
            ElapsedMilliseconds(
                indexingStartSeconds
            );

        entry.PreparationCursor =
            maximumIndex;

        if (
            entry.PreparationCursor <
            sourceTriangleIndices.Count
        )
        {
            return;
        }

        FinishSourcePreparation(
            entry
        );
    }

    private static void FinishSourcePreparation(
        SourceEntry entry
    )
    {
        for (
            int sectionIndex = 0;
            sectionIndex < entry.Sections.Length;
            sectionIndex++
        )
        {
            WireframeSection section =
                entry.Sections[
                    sectionIndex
                ];

            List<int> sectionIndices =
                entry.SectionTriangleIndices[
                    sectionIndex
                ];

            if (
                sectionIndices == null
                ||
                sectionIndices.Count <= 0
            )
            {
                if (!section.KnownEmpty)
                {
                    section.KnownEmpty =
                        true;

                    entry.KnownEmptySectionCount++;

                    knownEmptySectionCount++;
                }

                FinishQueuedSection(
                    section
                );

                continue;
            }

            section.FinishGeometryBounds();
        }

        entry.SourceTriangleIndices =
            null;

        entry.PreparationCursor =
            0;

        entry.PreparationState =
            SourcePreparationState.Ready;

        lastSourcePreparationMilliseconds =
            entry.SourcePreparationCpuMilliseconds;
    }

    // =====================================================
    // SECTION PROXY BUILD
    // =====================================================

    private static bool TryBuildSectionProxy(
        SourceEntry entry,
        WireframeSection section,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        double sectionBuildStartSeconds =
            EditorApplication.timeSinceStartup;

        List<Vector3> sourceVertices =
            entry.SourceVertices;

        List<Vector4> sourceClipmapData =
            entry.SourceClipmapData;

        List<int> sourceTriangles =
            entry.SectionTriangleIndices[
                section.Index
            ];

        if (
            sourceVertices == null
            ||
            sourceClipmapData == null
            ||
            sourceTriangles == null
            ||
            sourceTriangles.Count <= 0
        )
        {
            errorMessage =
                $"Lazy wireframe section {section.Index} for '{entry.SourceMesh.name}' has no prepared source data.";

            return false;
        }

        int proxyVertexCount =
            sourceTriangles.Count;

        List<Vector3> vertices =
            new List<Vector3>(
                proxyVertexCount
            );

        List<Vector4> clipmapData =
            new List<Vector4>(
                proxyVertexCount
            );

        List<Vector3> barycentricData =
            new List<Vector3>(
                proxyVertexCount
            );

        List<int> triangleIndices =
            new List<int>(
                proxyVertexCount
            );

        double vertexPreparationStartSeconds =
            EditorApplication.timeSinceStartup;

        using (VertexPreparationProfilerMarker.Auto())
        {
            for (
                int index = 0;
                index + 2 < sourceTriangles.Count;
                index += 3
            )
            {
                int sourceA =
                    sourceTriangles[index];

                int sourceB =
                    sourceTriangles[index + 1];

                int sourceC =
                    sourceTriangles[index + 2];

                if (
                    !IsValidSourceVertex(
                        sourceA,
                        sourceVertices,
                        sourceClipmapData
                    )
                    ||
                    !IsValidSourceVertex(
                        sourceB,
                        sourceVertices,
                        sourceClipmapData
                    )
                    ||
                    !IsValidSourceVertex(
                        sourceC,
                        sourceVertices,
                        sourceClipmapData
                    )
                )
                {
                    errorMessage =
                        $"Lazy wireframe section {section.Index} for '{entry.SourceMesh.name}' contains an invalid source vertex index.";

                    return false;
                }

                int localA =
                    vertices.Count;

                vertices.Add(
                    sourceVertices[sourceA]
                );

                clipmapData.Add(
                    sourceClipmapData[sourceA]
                );

                barycentricData.Add(
                    new Vector3(
                        1f,
                        0f,
                        0f
                    )
                );

                int localB =
                    vertices.Count;

                vertices.Add(
                    sourceVertices[sourceB]
                );

                clipmapData.Add(
                    sourceClipmapData[sourceB]
                );

                barycentricData.Add(
                    new Vector3(
                        0f,
                        1f,
                        0f
                    )
                );

                int localC =
                    vertices.Count;

                vertices.Add(
                    sourceVertices[sourceC]
                );

                clipmapData.Add(
                    sourceClipmapData[sourceC]
                );

                barycentricData.Add(
                    new Vector3(
                        0f,
                        0f,
                        1f
                    )
                );

                triangleIndices.Add(
                    localA
                );

                triangleIndices.Add(
                    localB
                );

                triangleIndices.Add(
                    localC
                );
            }
        }

        lastVertexPreparationMilliseconds =
            ElapsedMilliseconds(
                vertexPreparationStartSeconds
            );

        if (triangleIndices.Count <= 0)
        {
            errorMessage =
                $"Lazy wireframe section {section.Index} for '{entry.SourceMesh.name}' contains no renderable triangle topology.";

            return false;
        }

        double meshUploadStartSeconds =
            EditorApplication.timeSinceStartup;

        Mesh proxyMesh;

        using (MeshUploadProfilerMarker.Auto())
        {
            using (ProxyMeshCreationProfilerMarker.Auto())
            {
                proxyMesh =
                    new Mesh();

                proxyMesh.name =
                    $"WorldMeshes_Wireframe_{entry.SourceMesh.name}_{entry.RendererId}_Section{section.Index}";

                proxyMesh.hideFlags =
                    HideFlags.HideAndDontSave;

                proxyMesh.indexFormat =
                    vertices.Count > 65535
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16;
            }

            using (VertexUploadProfilerMarker.Auto())
            {
                proxyMesh.SetVertices(
                    vertices
                );

                /*
                 * TEXCOORD2 stores one-hot barycentric coordinates for
                 * Package 3 triangle-edge reconstruction in the fragment
                 * shader. TEXCOORD3 remains the existing clipmap transition
                 * weight channel copied exactly from the source mesh.
                 */
                proxyMesh.SetUVs(
                    2,
                    barycentricData
                );

                proxyMesh.SetUVs(
                    3,
                    clipmapData
                );
            }

            using (TriangleUploadProfilerMarker.Auto())
            {
                proxyMesh.subMeshCount =
                    1;

                proxyMesh.SetIndices(
                    triangleIndices,
                    MeshTopology.Triangles,
                    0,
                    false
                );
            }
        }

        lastMeshUploadMilliseconds =
            ElapsedMilliseconds(
                meshUploadStartSeconds
            );

        Bounds localBounds =
            CalculateConservativeLocalBounds(
                entry,
                section
            );

        proxyMesh.bounds =
            localBounds;

        section.ProxyMesh =
            proxyMesh;

        section.LastAppliedProxyBounds =
            localBounds;

        section.HasAppliedProxyBounds =
            true;

        int triangleCount =
            triangleIndices.Count /
            3;

        section.EdgeCount =
            triangleCount *
            3;

        builtSectionCount++;

        entry.BuiltSectionCount++;

        cachedEdgeCount +=
            section.EdgeCount;

        entry.CachedEdgeCount +=
            section.EdgeCount;

        if (entry.SectionTriangleIndices != null)
        {
            entry.SectionTriangleIndices[
                section.Index
            ] =
                null;
        }

        lastSectionBuildMilliseconds =
            ElapsedMilliseconds(
                sectionBuildStartSeconds
            );

        totalSectionBuildMilliseconds +=
            lastSectionBuildMilliseconds;

        profiledSectionBuildCount++;

        return true;
    }

    private static bool IsValidSourceVertex(
        int sourceIndex,
        List<Vector3> sourceVertices,
        List<Vector4> sourceClipmapData
    )
    {
        return
            sourceIndex >= 0
            &&
            sourceIndex < sourceVertices.Count
            &&
            sourceIndex < sourceClipmapData.Count;
    }

    private static void ReleaseSourceDataIfFullyBuilt(
        SourceEntry entry
    )
    {
        int renderableSectionCount =
            entry.Sections.Length -
            entry.KnownEmptySectionCount;

        if (
            entry.BuiltSectionCount <
            renderableSectionCount
        )
        {
            return;
        }

        entry.SourceVertices =
            null;

        entry.SourceClipmapData =
            null;

        entry.SectionTriangleIndices =
            null;

        entry.PreparationState =
            SourcePreparationState.FullyBuilt;
    }

    // =====================================================
    // SECTION DESCRIPTORS
    // =====================================================

    private static SourceEntry CreateSourceEntry(
        MeshRenderer renderer,
        Mesh sourceMesh,
        int lodLevel
    )
    {
        int triangleCount =
            CalculateTriangleIndexCount(
                sourceMesh
            ) /
            3;

        int sectionsPerAxis =
            ResolveSectionsPerAxis(
                triangleCount
            );

        Bounds sourceBounds =
            sourceMesh.bounds;

        WireframeSection[] sections =
            BuildSectionDescriptors(
                sourceBounds,
                sectionsPerAxis
            );

        return
            new SourceEntry(
                renderer,
                sourceMesh,
                lodLevel,
                sourceBounds,
                sectionsPerAxis,
                sections
            );
    }

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

    private static WireframeSection[] BuildSectionDescriptors(
        Bounds sourceBounds,
        int sectionsPerAxis
    )
    {
        int sectionCount =
            sectionsPerAxis *
            sectionsPerAxis;

        WireframeSection[] sections =
            new WireframeSection[
                sectionCount
            ];

        float width =
            sourceBounds.size.x /
            sectionsPerAxis;

        float depth =
            sourceBounds.size.z /
            sectionsPerAxis;

        float minimumX =
            sourceBounds.min.x;

        float minimumZ =
            sourceBounds.min.z;

        int sectionIndex =
            0;

        for (
            int z = 0;
            z < sectionsPerAxis;
            z++
        )
        {
            for (
                int x = 0;
                x < sectionsPerAxis;
                x++
            )
            {
                float cellMinimumX =
                    minimumX +
                    x *
                    width;

                float cellMaximumX =
                    x ==
                        sectionsPerAxis - 1
                        ? sourceBounds.max.x
                        : cellMinimumX +
                            width;

                float cellMinimumZ =
                    minimumZ +
                    z *
                    depth;

                float cellMaximumZ =
                    z ==
                        sectionsPerAxis - 1
                        ? sourceBounds.max.z
                        : cellMinimumZ +
                            depth;

                Bounds provisionalBounds =
                    new Bounds();

                provisionalBounds.SetMinMax(
                    new Vector3(
                        cellMinimumX,
                        sourceBounds.min.y,
                        cellMinimumZ
                    ),
                    new Vector3(
                        cellMaximumX,
                        sourceBounds.max.y,
                        cellMaximumZ
                    )
                );

                /*
                 * Before the first source indexing pass we know only the
                 * descriptor cell, not each triangle's exact extent. One
                 * whole cell of X/Z padding keeps lazy-build eligibility
                 * conservative. Exact triangle bounds replace this after
                 * preparation completes.
                 */
                if (sectionsPerAxis > 1)
                {
                    provisionalBounds.Expand(
                        new Vector3(
                            width * 2f,
                            0f,
                            depth * 2f
                        )
                    );
                }

                sections[
                    sectionIndex
                ] =
                    new WireframeSection(
                        sectionIndex,
                        provisionalBounds
                    );

                sectionIndex++;
            }
        }

        return
            sections;
    }

    private static int ResolveSectionIndex(
        SourceEntry entry,
        Vector3 localPosition
    )
    {
        int sectionsPerAxis =
            entry.SectionsPerAxis;

        if (sectionsPerAxis <= 1)
        {
            return 0;
        }

        Bounds sourceBounds =
            entry.SourceMeshBounds;

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

    // =====================================================
    // BOUNDS
    // =====================================================

    private static Bounds CalculateConservativeLocalBounds(
        SourceEntry entry,
        WireframeSection section
    )
    {
        Bounds geometryBounds =
            section.HasExactGeometryBounds
                ? section.ExactGeometryBounds
                : section.ProvisionalBounds;

        Bounds sourceRendererBounds =
            entry.SourceRenderer.localBounds;

        Bounds sourceMeshBounds =
            entry.SourceMeshBounds;

        Vector3 minimum =
            geometryBounds.min;

        Vector3 maximum =
            geometryBounds.max;

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

    private static void ApplyProxyBoundsIfNeeded(
        WireframeSection section,
        Bounds localBounds
    )
    {
        if (
            section.ProxyMesh == null
            ||
            (
                section.HasAppliedProxyBounds
                &&
                section.LastAppliedProxyBounds.Equals(
                    localBounds
                )
            )
        )
        {
            return;
        }

        section.ProxyMesh.bounds =
            localBounds;

        section.LastAppliedProxyBounds =
            localBounds;

        section.HasAppliedProxyBounds =
            true;
    }

    // =====================================================
    // RESOURCE RELEASE
    // =====================================================

    public static void DestroyAll()
    {
        foreach (
            SourceEntry entry
            in entries.Values
        )
        {
            DestroyEntry(
                entry
            );
        }

        entries.Clear();
        pendingBuilds.Clear();

        activeBuildRequest =
            null;

        totalSectionDescriptorCount =
            0;

        knownEmptySectionCount =
            0;

        builtSectionCount =
            0;

        cachedEdgeCount =
            0;

        pendingBuildCount =
            0;

        TerrainAuthoringWireframeCulling
            .ResetRenderedDiagnostics();
    }

    private static void DestroyEntry(
        SourceEntry entry
    )
    {
        if (entry == null)
        {
            return;
        }

        entry.IsAlive =
            false;

        foreach (
            WireframeSection section
            in entry.Sections
        )
        {
            if (section.BuildQueued)
            {
                FinishQueuedSection(
                    section
                );
            }

            if (section.ProxyMesh != null)
            {
                Object.DestroyImmediate(
                    section.ProxyMesh
                );

                section.ProxyMesh =
                    null;
            }
        }

        totalSectionDescriptorCount =
            Mathf.Max(
                0,
                totalSectionDescriptorCount -
                entry.Sections.Length
            );

        knownEmptySectionCount =
            Mathf.Max(
                0,
                knownEmptySectionCount -
                entry.KnownEmptySectionCount
            );

        builtSectionCount =
            Mathf.Max(
                0,
                builtSectionCount -
                entry.BuiltSectionCount
            );

        cachedEdgeCount =
            Mathf.Max(
                0,
                cachedEdgeCount -
                entry.CachedEdgeCount
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

    private static int CalculateTriangleIndexCount(
        Mesh sourceMesh
    )
    {
        if (sourceMesh == null)
        {
            return 0;
        }

        long count =
            0;

        for (
            int subMeshIndex = 0;
            subMeshIndex < sourceMesh.subMeshCount;
            subMeshIndex++
        )
        {
            if (
                sourceMesh.GetTopology(
                    subMeshIndex
                ) ==
                MeshTopology.Triangles
            )
            {
                count +=
                    (long)sourceMesh.GetIndexCount(
                        subMeshIndex
                    );
            }
        }

        return
            count > int.MaxValue
                ? int.MaxValue
                : (int)count;
    }

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

    private static void CompleteEnableMeasurementIfNeeded()
    {
        if (!enableLatencyPending)
        {
            return;
        }

        firstVisibleLatencyMilliseconds =
            ElapsedMilliseconds(
                enableLatencyStartSeconds
            );

        enableLatencyPending =
            false;
    }

    private static double ElapsedMilliseconds(
        double startSeconds
    )
    {
        return
            (
                EditorApplication.timeSinceStartup -
                startSeconds
            )
            *
            1000.0;
    }

    // =====================================================
    // DATA TYPES
    // =====================================================

    private enum SourcePreparationState
    {
        Unprepared,
        Indexing,
        Ready,
        FullyBuilt,
        Failed
    }

    private sealed class SourceEntry
    {
        public readonly int RendererId;

        public readonly MeshRenderer SourceRenderer;

        public readonly Mesh SourceMesh;

        public readonly MeshFilter SourceMeshFilter;

        public readonly Bounds SourceMeshBounds;

        public readonly int SectionsPerAxis;

        public readonly WireframeSection[] Sections;

        public int LODLevel;

        public bool IsAlive =
            true;

        public SourcePreparationState PreparationState =
            SourcePreparationState.Unprepared;

        public List<Vector3> SourceVertices;

        public List<Vector4> SourceClipmapData;

        public List<int> SourceTriangleIndices;

        public List<int>[] SectionTriangleIndices;

        public int PreparationCursor;

        public double SourcePreparationCpuMilliseconds;

        public int KnownEmptySectionCount;

        public int BuiltSectionCount;

        public int CachedEdgeCount;

        public SourceEntry(
            MeshRenderer sourceRenderer,
            Mesh sourceMesh,
            int lodLevel,
            Bounds sourceMeshBounds,
            int sectionsPerAxis,
            WireframeSection[] sections
        )
        {
            RendererId =
                sourceRenderer.GetInstanceID();

            SourceRenderer =
                sourceRenderer;

            SourceMesh =
                sourceMesh;

            SourceMeshFilter =
                sourceRenderer
                    .GetComponent<MeshFilter>();

            LODLevel =
                lodLevel;

            SourceMeshBounds =
                sourceMeshBounds;

            SectionsPerAxis =
                sectionsPerAxis;

            Sections =
                sections;
        }
    }

    private sealed class WireframeSection
    {
        public readonly int Index;

        public readonly Bounds ProvisionalBounds;

        public Mesh ProxyMesh;

        public int EdgeCount;

        public bool KnownEmpty;

        public bool BuildFailed;

        public bool BuildQueued;

        public bool VisibleThisRepaint;

        public bool HasExactGeometryBounds;

        public Bounds ExactGeometryBounds;

        public bool HasAppliedProxyBounds;

        public Bounds LastAppliedProxyBounds;

        private bool hasGeometryPoint;

        private Vector3 geometryMinimum;

        private Vector3 geometryMaximum;

        public WireframeSection(
            int index,
            Bounds provisionalBounds
        )
        {
            Index =
                index;

            ProvisionalBounds =
                provisionalBounds;
        }

        public void EncapsulateGeometry(
            Vector3 position
        )
        {
            if (!hasGeometryPoint)
            {
                geometryMinimum =
                    position;

                geometryMaximum =
                    position;

                hasGeometryPoint =
                    true;

                return;
            }

            geometryMinimum =
                Vector3.Min(
                    geometryMinimum,
                    position
                );

            geometryMaximum =
                Vector3.Max(
                    geometryMaximum,
                    position
                );
        }

        public void FinishGeometryBounds()
        {
            if (!hasGeometryPoint)
            {
                return;
            }

            Bounds bounds =
                new Bounds();

            bounds.SetMinMax(
                geometryMinimum,
                geometryMaximum
            );

            ExactGeometryBounds =
                bounds;

            HasExactGeometryBounds =
                true;
        }
    }

    private sealed class SectionBuildRequest
    {
        public readonly SourceEntry Entry;

        public readonly WireframeSection Section;

        public SectionBuildRequest(
            SourceEntry entry,
            WireframeSection section
        )
        {
            Entry =
                entry;

            Section =
                section;
        }
    }
}
