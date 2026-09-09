using UnityEditor;
using UnityEngine;

public enum TerrainAuthoringWireframeSectionVisibility
{
    Visible,
    LODCulled,
    DistanceCulled,
    FrustumCulled
}

/*
 * Editor-only visibility policy for spatial true-wireframe sections.
 *
 * Package 2 keeps the Package 1 preference surface, but moves the
 * submission decision from whole source renderers to independently
 * cullable section bounds. The current Scene View camera state is cached
 * once per repaint so section tests do not touch EditorPrefs or allocate
 * frustum-plane arrays inside the hot loop.
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
    // MOST-RECENT REPAINT STATE
    // =====================================================

    private static readonly Plane[] repaintFrustumPlanes =
        new Plane[6];

    private static int repaintMaximumLOD =
        DefaultMaximumLOD;

    private static bool repaintDistanceLimitEnabled;

    private static double repaintMaximumDistanceSquared;

    private static Vector3 repaintCameraPosition;

    private static bool repaintCameraReady;

    // =====================================================
    // MOST-RECENT REPAINT DIAGNOSTICS
    // =====================================================

    private static int renderedSectionCount;

    private static int renderedEdgeCount;

    private static int lodCulledSectionCount;

    private static int distanceCulledSectionCount;

    private static int frustumCulledSectionCount;

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

    public static int RenderedSectionCount
    {
        get
        {
            return
                renderedSectionCount;
        }
    }

    /*
     * Backwards-compatible Package 1 diagnostic name.
     * A rendered proxy is now one rendered spatial section.
     */
    public static int RenderedProxyMeshCount
    {
        get
        {
            return
                renderedSectionCount;
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

    public static int LODCulledSectionCount
    {
        get
        {
            return
                lodCulledSectionCount;
        }
    }

    public static int LODCulledProxyCount
    {
        get
        {
            return
                lodCulledSectionCount;
        }
    }

    public static int DistanceCulledSectionCount
    {
        get
        {
            return
                distanceCulledSectionCount;
        }
    }

    public static int DistanceCulledProxyCount
    {
        get
        {
            return
                distanceCulledSectionCount;
        }
    }

    public static int FrustumCulledSectionCount
    {
        get
        {
            return
                frustumCulledSectionCount;
        }
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

        if (!repaintCameraReady)
        {
            repaintCameraPosition =
                Vector3.zero;

            return;
        }

        repaintCameraPosition =
            camera.transform.position;

        GeometryUtility.CalculateFrustumPlanes(
            camera,
            repaintFrustumPlanes
        );
    }

    public static TerrainAuthoringWireframeSectionVisibility
        EvaluateSection(
            int lodLevel,
            Bounds worldBounds
        )
    {
        TerrainAuthoringWireframeSectionVisibility visibility =
            EvaluateSectionWithoutDiagnostics(
                lodLevel,
                worldBounds
            );

        switch (visibility)
        {
            case TerrainAuthoringWireframeSectionVisibility.LODCulled:
                lodCulledSectionCount++;
                break;

            case TerrainAuthoringWireframeSectionVisibility.DistanceCulled:
                distanceCulledSectionCount++;
                break;

            case TerrainAuthoringWireframeSectionVisibility.FrustumCulled:
                frustumCulledSectionCount++;
                break;
        }

        return
            visibility;
    }

    public static bool IsPotentiallyVisible(
        int lodLevel,
        Bounds worldBounds
    )
    {
        return
            EvaluateSectionWithoutDiagnostics(
                lodLevel,
                worldBounds
            ) ==
            TerrainAuthoringWireframeSectionVisibility.Visible;
    }

    public static void RecordRendered(
        int edgeCount
    )
    {
        renderedSectionCount++;

        renderedEdgeCount +=
            Mathf.Max(
                0,
                edgeCount
            );
    }

    public static void ResetRenderedDiagnostics()
    {
        renderedSectionCount =
            0;

        renderedEdgeCount =
            0;

        lodCulledSectionCount =
            0;

        distanceCulledSectionCount =
            0;

        frustumCulledSectionCount =
            0;
    }

    // =====================================================
    // INTERNAL VISIBILITY
    // =====================================================

    private static TerrainAuthoringWireframeSectionVisibility
        EvaluateSectionWithoutDiagnostics(
            int lodLevel,
            Bounds worldBounds
        )
    {
        if (!repaintCameraReady)
        {
            return
                TerrainAuthoringWireframeSectionVisibility.FrustumCulled;
        }

        if (
            lodLevel >
            repaintMaximumLOD
        )
        {
            return
                TerrainAuthoringWireframeSectionVisibility.LODCulled;
        }

        if (
            repaintDistanceLimitEnabled
            &&
            (double)worldBounds.SqrDistance(
                repaintCameraPosition
            ) >
            repaintMaximumDistanceSquared
        )
        {
            return
                TerrainAuthoringWireframeSectionVisibility.DistanceCulled;
        }

        if (
            !GeometryUtility.TestPlanesAABB(
                repaintFrustumPlanes,
                worldBounds
            )
        )
        {
            return
                TerrainAuthoringWireframeSectionVisibility.FrustumCulled;
        }

        return
            TerrainAuthoringWireframeSectionVisibility.Visible;
    }

    // =====================================================
    // HELPERS
    // =====================================================

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
