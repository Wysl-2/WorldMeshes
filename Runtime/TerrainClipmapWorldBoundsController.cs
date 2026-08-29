using UnityEngine;

/*
 * Persistent authority for the logical terrain rectangle used by
 * clipmap rendering.
 *
 * This component runs in both Edit Mode and Play Mode so world
 * clipping remains valid even while no height cache is available.
 */
[ExecuteAlways]
[DisallowMultipleComponent]
public class TerrainClipmapWorldBoundsController :
    MonoBehaviour
{
    // =====================================================
    // SOURCE DATA
    // =====================================================

    [Header("Source Data")]

    [SerializeField]
    private WorldSettings worldSettings;

    // =====================================================
    // DEBUG
    // =====================================================

    [Header("Debug")]

    [SerializeField]
    private bool logBindingChanges =
        false;

    // =====================================================
    // STATE
    // =====================================================

    private Vector2 lastAppliedWorldSizeXZ =
        Vector2.zero;

    private bool bindingApplied;

    private bool hasWarnedBindingFailure;

    // =====================================================
    // PUBLIC STATE
    // =====================================================

    public bool BindingApplied
    {
        get
        {
            return
                bindingApplied;
        }
    }

    public Vector2 WorldSizeXZ
    {
        get
        {
            return
                TerrainClipmapLayoutUtility
                    .CalculateWorldSizeXZ(
                        worldSettings
                    );
        }
    }

    // =====================================================
    // CONFIGURE
    // =====================================================

    public bool Configure(
        WorldSettings settings
    )
    {
        bool changed =
            worldSettings !=
            settings;

        if (!changed)
        {
            return false;
        }

        worldSettings =
            settings;

        bindingApplied =
            false;

        lastAppliedWorldSizeXZ =
            Vector2.zero;

        hasWarnedBindingFailure =
            false;

        if (isActiveAndEnabled)
        {
            ApplyWorldBounds();
        }

        return true;
    }

    // =====================================================
    // ENABLE / DISABLE
    // =====================================================

    private void OnEnable()
    {
        bindingApplied =
            false;

        lastAppliedWorldSizeXZ =
            Vector2.zero;

        hasWarnedBindingFailure =
            false;

        ApplyWorldBounds();
    }

    private void OnDisable()
    {
        TerrainClipmapWorldBoundsBindingUtility
            .Disable(
                transform
            );

        bindingApplied =
            false;

        lastAppliedWorldSizeXZ =
            Vector2.zero;
    }

    // =====================================================
    // UPDATE
    // =====================================================

    /*
     * WorldSettings is a ScriptableObject. Changing its grid size
     * does not invoke OnValidate on this component, so perform a
     * tiny change check here.
     *
     * Renderer traversal only occurs when the calculated world
     * dimensions actually changed or when binding is invalid.
     */
    private void Update()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (worldSettings == null)
        {
            if (bindingApplied)
            {
                ApplyWorldBounds();
            }

            return;
        }

        Vector2 currentWorldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        bool sizeChanged =
            !Approximately(
                currentWorldSizeXZ,
                lastAppliedWorldSizeXZ
            );

        if (
            !bindingApplied
            ||
            sizeChanged
        )
        {
            ApplyWorldBounds();
        }
    }

    // =====================================================
    // APPLY WORLD BOUNDS
    // =====================================================

    [ContextMenu("Apply Clipmap World Bounds")]
    public bool ApplyWorldBounds()
    {
        if (worldSettings == null)
        {
            TerrainClipmapWorldBoundsBindingUtility
                .Disable(
                    transform
                );

            bindingApplied =
                false;

            lastAppliedWorldSizeXZ =
                Vector2.zero;

            return false;
        }

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        bool success =
            TerrainClipmapWorldBoundsBindingUtility
                .TryBind(
                    transform,
                    worldSizeXZ,
                    out int boundRendererCount,
                    out string errorMessage
                );

        if (!success)
        {
            bindingApplied =
                false;

            /*
             * During Sync World Hierarchy this component can be
             * added/configured before the generated child renderers
             * are recreated. That temporary state is expected.
             *
             * Suppress the warning only when Edit Mode currently
             * has no child MeshRenderers at all. Once renderers
             * exist, shader/binding incompatibilities are useful
             * diagnostics.
             */
            bool shouldWarn =
                Application.isPlaying
                ||
                HasAnyChildMeshRenderer();

            if (
                shouldWarn
                &&
                !hasWarnedBindingFailure
            )
            {
                Debug.LogWarning(
                    "TerrainClipmapWorldBoundsController could " +
                    "not bind the logical terrain boundary.\n\n" +
                    errorMessage +
                    "\n\nRun Sync World Hierarchy if the " +
                    "clipmap renderers are missing or outdated.",
                    this
                );

                hasWarnedBindingFailure =
                    true;
            }

            return false;
        }

        bindingApplied =
            true;

        lastAppliedWorldSizeXZ =
            worldSizeXZ;

        hasWarnedBindingFailure =
            false;

        if (logBindingChanges)
        {
            Debug.Log(
                "Clipmap logical world bounds applied.\n\n" +
                $"World Size XZ: " +
                $"{worldSizeXZ.x:R} x " +
                $"{worldSizeXZ.y:R}\n" +
                $"Renderers: {boundRendererCount}",
                this
            );
        }

        return true;
    }

    // =====================================================
    // FORCE REBIND
    // =====================================================

    /*
     * Useful after generated renderer hierarchy changes while the
     * world dimensions themselves remain unchanged.
     */
    public void InvalidateBinding()
    {
        bindingApplied =
            false;
    }

    // =====================================================
    // HELPERS
    // =====================================================


    private bool HasAnyChildMeshRenderer()
    {
        MeshRenderer[] renderers =
            GetComponentsInChildren<MeshRenderer>(
                true
            );

        return
            renderers != null
            &&
            renderers.Length > 0;
    }

    private static bool Approximately(
        Vector2 a,
        Vector2 b
    )
    {
        return
            Mathf.Approximately(
                a.x,
                b.x
            )
            &&
            Mathf.Approximately(
                a.y,
                b.y
            );
    }
}
