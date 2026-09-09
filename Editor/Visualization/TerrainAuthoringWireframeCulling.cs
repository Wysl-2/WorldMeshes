using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Editor-only submission policy for the true displaced wireframe.
 *
 * TerrainAuthoringWireframeRenderer continues to own proxy creation,
 * materials, and Graphics.RenderMesh calls. This helper owns only:
 *
 * - editor preference-backed visibility limits
 * - cached per-renderer LOD / hollow-ring distance metadata
 * - lightweight Scene View submission decisions
 * - most-recent-repaint diagnostics
 */
public static class TerrainAuthoringWireframeCulling
{
    // =====================================================
    // EDITOR PREFS
    // =====================================================

    private const string MaximumLODEditorPrefsKey =
        "WorldMeshes.AuthoringWireframe.MaximumLOD";

    private const string DistanceLimitEnabledEditorPrefsKey =
        "WorldMeshes.AuthoringWireframe.DistanceLimitEnabled";

    private const string MaximumDistanceEditorPrefsKey =
        "WorldMeshes.AuthoringWireframe.MaximumDistance";

    private const int DefaultMaximumLOD =
        9;

    private const float DefaultMaximumDistance =
        1000f;

    // =====================================================
    // CACHED RENDERER METADATA
    // =====================================================

    private static readonly Dictionary<int, RendererMetadata>
        metadataByRendererId =
            new Dictionary<int, RendererMetadata>();

    // =====================================================
    // MOST-RECENT REPAINT DIAGNOSTICS
    // =====================================================

    private static int renderedProxyMeshCount;

    private static int renderedEdgeCount;

    private static int lodCulledProxyCount;

    private static int distanceCulledProxyCount;

    private static int repaintMaximumLOD =
        DefaultMaximumLOD;

    private static bool repaintDistanceLimitEnabled;

    private static double repaintMaximumDistanceSquared;

    private static Vector3 repaintCameraPosition;

    private static bool repaintCameraReady;

    // =====================================================
    // PUBLIC SETTINGS
    // =====================================================

    public static int MaximumLOD
    {
        get
        {
            return
                Mathf.Clamp(
                    EditorPrefs.GetInt(
                        MaximumLODEditorPrefsKey,
                        DefaultMaximumLOD
                    ),
                    0,
                    DefaultMaximumLOD
                );
        }

        set
        {
            int safeValue =
                Mathf.Clamp(
                    value,
                    0,
                    DefaultMaximumLOD
                );

            if (MaximumLOD == safeValue)
            {
                return;
            }

            EditorPrefs.SetInt(
                MaximumLODEditorPrefsKey,
                safeValue
            );

            RepaintEditorViews();
        }
    }

    public static bool DistanceLimitEnabled
    {
        get
        {
            return
                EditorPrefs.GetBool(
                    DistanceLimitEnabledEditorPrefsKey,
                    false
                );
        }

        set
        {
            if (DistanceLimitEnabled == value)
            {
                return;
            }

            EditorPrefs.SetBool(
                DistanceLimitEnabledEditorPrefsKey,
                value
            );

            RepaintEditorViews();
        }
    }

    public static float MaximumDistance
    {
        get
        {
            float storedValue =
                EditorPrefs.GetFloat(
                    MaximumDistanceEditorPrefsKey,
                    DefaultMaximumDistance
                );

            if (!IsFinite(storedValue))
            {
                return
                    DefaultMaximumDistance;
            }

            return
                Mathf.Max(
                    0.01f,
                    storedValue
                );
        }

        set
        {
            float safeValue =
                !IsFinite(value)
                    ? DefaultMaximumDistance
                    : Mathf.Max(
                        0.01f,
                        value
                    );

            if (
                Mathf.Approximately(
                    MaximumDistance,
                    safeValue
                )
            )
            {
                return;
            }

            EditorPrefs.SetFloat(
                MaximumDistanceEditorPrefsKey,
                safeValue
            );

            RepaintEditorViews();
        }
    }

    // =====================================================
    // PUBLIC DIAGNOSTICS
    // =====================================================

    public static int RenderedProxyMeshCount
    {
        get
        {
            return
                renderedProxyMeshCount;
        }
    }

    public static int RenderedEdgeCount
    {
        get
        {
            return
                renderedEdgeCount;
        }
    }

    public static int LODCulledProxyCount
    {
        get
        {
            return
                lodCulledProxyCount;
        }
    }

    public static int DistanceCulledProxyCount
    {
        get
        {
            return
                distanceCulledProxyCount;
        }
    }

    // =====================================================
    // PROXY METADATA SYNCHRONIZATION
    // =====================================================

    public static void UpdateLOD(
        MeshRenderer renderer,
        Transform clipmapRoot
    )
    {
        if (renderer == null)
        {
            return;
        }

        RendererMetadata metadata =
            GetOrCreateMetadata(
                renderer.GetInstanceID()
            );

        metadata.lodLevel =
            DetermineLODLevel(
                renderer.transform,
                clipmapRoot
            );
    }

    public static void RegisterGeometry(
        MeshRenderer renderer,
        List<Vector3> vertices,
        List<int> lineIndices
    )
    {
        if (renderer == null)
        {
            return;
        }

        RendererMetadata metadata =
            GetOrCreateMetadata(
                renderer.GetInstanceID()
            );

        float rawInnerHalfExtent =
            CalculateInnerHalfExtentXZ(
                vertices
            );

        /*
         * Stitch vertices on the adaptive fine edge can shift by one
         * fine-grid sample at runtime/editor follow positions.
         *
         * Subtract one smallest source edge as a conservative inward
         * safety margin. Ring meshes receive a similarly conservative
         * margin, which is preferable to incorrectly distance-culling
         * geometry close to the configured threshold.
         */
        float inwardSafetyMargin =
            CalculateMinimumHorizontalEdgeLength(
                vertices,
                lineIndices
            );

        metadata.innerHalfExtentXZ =
            Mathf.Max(
                0f,
                rawInnerHalfExtent -
                    inwardSafetyMargin
            );
    }

    public static void RemoveRenderer(
        int rendererId
    )
    {
        metadataByRendererId.Remove(
            rendererId
        );
    }

    public static void ClearCachedMetadata()
    {
        metadataByRendererId.Clear();

        ResetRenderedDiagnostics();

        repaintCameraReady =
            false;
    }

    // =====================================================
    // SCENE VIEW REPAINT
    // =====================================================

    public static void BeginSceneViewRepaint(
        Camera camera
    )
    {
        ResetRenderedDiagnostics();

        repaintMaximumLOD =
            MaximumLOD;

        repaintDistanceLimitEnabled =
            DistanceLimitEnabled;

        float maximumDistance =
            MaximumDistance;

        repaintMaximumDistanceSquared =
            (double)maximumDistance *
            maximumDistance;

        repaintCameraReady =
            camera != null;

        repaintCameraPosition =
            repaintCameraReady
                ? camera.transform.position
                : Vector3.zero;
    }

    public static bool ShouldRender(
        MeshRenderer renderer
    )
    {
        if (
            renderer == null
            ||
            !repaintCameraReady
        )
        {
            return false;
        }

        int rendererId =
            renderer.GetInstanceID();

        RendererMetadata metadata;

        if (
            !metadataByRendererId.TryGetValue(
                rendererId,
                out metadata
            )
            ||
            metadata == null
        )
        {
            /*
             * Missing metadata should never make an otherwise valid
             * cached proxy disappear. Preserve the pre-culling behavior
             * until the next normal proxy synchronization fills it in.
             */
            return true;
        }

        if (
            metadata.lodLevel >
            repaintMaximumLOD
        )
        {
            lodCulledProxyCount++;

            return false;
        }

        if (!repaintDistanceLimitEnabled)
        {
            return true;
        }

        float distanceSquared =
            CalculateDistanceSquaredToRenderer(
                renderer,
                metadata,
                repaintCameraPosition
            );

        if (
            (double)distanceSquared >
            repaintMaximumDistanceSquared
        )
        {
            distanceCulledProxyCount++;

            return false;
        }

        return true;
    }

    public static void RecordRendered(
        int edgeCount
    )
    {
        renderedProxyMeshCount++;

        renderedEdgeCount +=
            Mathf.Max(
                0,
                edgeCount
            );
    }

    // =====================================================
    // DISTANCE
    // =====================================================

    private static float CalculateDistanceSquaredToRenderer(
        MeshRenderer renderer,
        RendererMetadata metadata,
        Vector3 cameraPosition
    )
    {
        /*
         * Renderer.bounds already contains the conservative displaced
         * Y range and any stitch X/Z bounds expansion maintained by the
         * existing clipmap systems.
         */
        float distanceSquared =
            renderer.bounds.SqrDistance(
                cameraPosition
            );

        float innerHalfExtent =
            metadata.innerHalfExtentXZ;

        if (innerHalfExtent <= 0f)
        {
            return
                distanceSquared;
        }

        /*
         * A hollow ring's outer AABB commonly contains the Scene View
         * camera even when the nearest actual ring edge is far away.
         * Account for that empty central square using metadata cached
         * once during proxy construction.
         */
        Transform rendererTransform =
            renderer.transform;

        Vector3 localCameraPosition =
            rendererTransform
                .InverseTransformPoint(
                    cameraPosition
                );

        float absoluteX =
            Mathf.Abs(
                localCameraPosition.x
            );

        float absoluteZ =
            Mathf.Abs(
                localCameraPosition.z
            );

        if (
            absoluteX >=
                innerHalfExtent
            ||
            absoluteZ >=
                innerHalfExtent
        )
        {
            return
                distanceSquared;
        }

        Vector3 scale =
            rendererTransform.lossyScale;

        float distanceToInnerX =
            (
                innerHalfExtent -
                absoluteX
            )
            *
            Mathf.Max(
                0.0001f,
                Mathf.Abs(
                    scale.x
                )
            );

        float distanceToInnerZ =
            (
                innerHalfExtent -
                absoluteZ
            )
            *
            Mathf.Max(
                0.0001f,
                Mathf.Abs(
                    scale.z
                )
            );

        float distanceToInnerBoundary =
            Mathf.Min(
                distanceToInnerX,
                distanceToInnerZ
            );

        return
            distanceSquared
            +
            distanceToInnerBoundary *
            distanceToInnerBoundary;
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
    // GEOMETRY METADATA
    // =====================================================

    private static float CalculateInnerHalfExtentXZ(
        List<Vector3> vertices
    )
    {
        if (
            vertices == null
            ||
            vertices.Count <= 0
        )
        {
            return 0f;
        }

        float minimumRadius =
            float.PositiveInfinity;

        for (
            int index = 0;
            index < vertices.Count;
            index++
        )
        {
            Vector3 vertex =
                vertices[index];

            float radius =
                Mathf.Max(
                    Mathf.Abs(
                        vertex.x
                    ),
                    Mathf.Abs(
                        vertex.z
                    )
                );

            if (
                radius <
                minimumRadius
            )
            {
                minimumRadius =
                    radius;
            }
        }

        if (!IsFinite(minimumRadius))
        {
            return 0f;
        }

        return
            Mathf.Max(
                0f,
                minimumRadius
            );
    }

    private static float CalculateMinimumHorizontalEdgeLength(
        List<Vector3> vertices,
        List<int> lineIndices
    )
    {
        if (
            vertices == null
            ||
            lineIndices == null
            ||
            lineIndices.Count < 2
        )
        {
            return 0f;
        }

        float minimumLengthSquared =
            float.PositiveInfinity;

        for (
            int index = 0;
            index + 1 < lineIndices.Count;
            index += 2
        )
        {
            int indexA =
                lineIndices[index];

            int indexB =
                lineIndices[index + 1];

            if (
                indexA < 0
                ||
                indexA >= vertices.Count
                ||
                indexB < 0
                ||
                indexB >= vertices.Count
            )
            {
                continue;
            }

            Vector3 a =
                vertices[indexA];

            Vector3 b =
                vertices[indexB];

            float deltaX =
                b.x -
                a.x;

            float deltaZ =
                b.z -
                a.z;

            float lengthSquared =
                deltaX *
                    deltaX
                +
                deltaZ *
                    deltaZ;

            if (
                lengthSquared >
                    0.00000001f
                &&
                lengthSquared <
                    minimumLengthSquared
            )
            {
                minimumLengthSquared =
                    lengthSquared;
            }
        }

        if (!IsFinite(minimumLengthSquared))
        {
            return 0f;
        }

        return
            Mathf.Sqrt(
                minimumLengthSquared
            );
    }

    // =====================================================
    // METADATA STORAGE
    // =====================================================

    private static RendererMetadata GetOrCreateMetadata(
        int rendererId
    )
    {
        if (
            metadataByRendererId.TryGetValue(
                rendererId,
                out RendererMetadata metadata
            )
            &&
            metadata != null
        )
        {
            return
                metadata;
        }

        metadata =
            new RendererMetadata();

        metadataByRendererId[
            rendererId
        ] =
            metadata;

        return
            metadata;
    }

    private sealed class RendererMetadata
    {
        public int lodLevel;

        public float innerHalfExtentXZ;
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static void ResetRenderedDiagnostics()
    {
        renderedProxyMeshCount =
            0;

        renderedEdgeCount =
            0;

        lodCulledProxyCount =
            0;

        distanceCulledProxyCount =
            0;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }

    private static void RepaintEditorViews()
    {
        SceneView.RepaintAll();

        WorldMeshesEditorWindow[] windows =
            Resources
                .FindObjectsOfTypeAll<WorldMeshesEditorWindow>();

        foreach (
            WorldMeshesEditorWindow window
            in windows
        )
        {
            if (window != null)
            {
                window.Repaint();
            }
        }
    }
}
