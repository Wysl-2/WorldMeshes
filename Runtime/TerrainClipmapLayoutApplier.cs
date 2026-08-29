using UnityEngine;

/*
 * Shared clipmap hierarchy mutator.
 *
 * TerrainClipmapLayoutUtility answers:
 *
 *     "Where should every LOD be?"
 *
 * TerrainClipmapLayoutApplier answers:
 *
 *     "Apply that calculated layout to this generated clipmap."
 *
 * It contains no Player, Scene View, streaming, or authoring
 * policy. Runtime and editor controllers can therefore share the
 * exact same hierarchy/stitch application behavior.
 */
public sealed class TerrainClipmapLayoutApplier
{
    // =====================================================
    // SHADER PROPERTY IDS
    // =====================================================

    private static readonly int
        ClipmapTransitionOffsetPropertyId =
            Shader.PropertyToID(
                "_ClipmapTransitionOffset"
            );

    // =====================================================
    // CONFIGURATION
    // =====================================================

    private Transform clipmapRoot;

    private WorldSettings worldSettings;

    // =====================================================
    // GENERATED HIERARCHY REFERENCES
    // =====================================================

    /*
     * Index 0 is unused because LOD0 is the clipmap root.
     *
     * Index N references direct child:
     *
     *     LODN
     */
    private Transform[] lodLevelTransforms;

    /*
     * Index N references:
     *
     *     Stitch_LOD(N-1)_LODN
     *
     * under generated child LODN.
     */
    private MeshRenderer[] stitchRenderers;

    private MaterialPropertyBlock
        transitionPropertyBlock;

    private bool hierarchyReferencesValid;

    // =====================================================
    // CONFIGURE
    // =====================================================

    public bool Configure(
        Transform newClipmapRoot,
        WorldSettings newWorldSettings
    )
    {
        bool changed =
            clipmapRoot !=
                newClipmapRoot
            ||
            worldSettings !=
                newWorldSettings;

        if (!changed)
        {
            return false;
        }

        clipmapRoot =
            newClipmapRoot;

        worldSettings =
            newWorldSettings;

        InvalidateHierarchyReferences();

        return true;
    }

    // =====================================================
    // APPLY
    // =====================================================

    public bool TryApply(
        TerrainClipmapLayout layout,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (!TryValidateConfiguration(
            layout,
            out errorMessage
        ))
        {
            return false;
        }

        if (
            !TryEnsureHierarchyReferences(
                out errorMessage
            )
        )
        {
            return false;
        }

        Vector3 lod0Anchor =
            layout.GetAnchor(
                0
            );

        // -------------------------------------------------
        // LOD0 / clipmap root
        // -------------------------------------------------

        if (
            clipmapRoot.position !=
            lod0Anchor
        )
        {
            clipmapRoot.position =
                lod0Anchor;
        }

        // -------------------------------------------------
        // Coarse LOD groups
        // -------------------------------------------------

        for (
            int level = 1;
            level < layout.LevelCount;
            level++
        )
        {
            Transform levelTransform =
                lodLevelTransforms[
                    level
                ];

            Vector3 localOffset =
                layout.GetAnchor(
                    level
                )
                -
                lod0Anchor;

            /*
             * Generated clipmap hierarchy keeps every LOD group
             * at the root Y. Only horizontal independent snapping
             * differs between LODs.
             */
            localOffset.y =
                0f;

            if (
                levelTransform.localPosition !=
                localOffset
            )
            {
                levelTransform.localPosition =
                    localOffset;
            }

            if (
                levelTransform.localRotation !=
                Quaternion.identity
            )
            {
                levelTransform.localRotation =
                    Quaternion.identity;
            }

            if (
                levelTransform.localScale !=
                Vector3.one
            )
            {
                levelTransform.localScale =
                    Vector3.one;
            }
        }

        // -------------------------------------------------
        // Adaptive stitch shader offsets
        // -------------------------------------------------

        EnsureTransitionPropertyBlock();

        for (
            int coarseLevel = 1;
            coarseLevel < layout.LevelCount;
            coarseLevel++
        )
        {
            MeshRenderer stitchRenderer =
                stitchRenderers[
                    coarseLevel
                ];

            Vector3 fineMinusCoarse =
                layout.GetAnchor(
                    coarseLevel - 1
                )
                -
                layout.GetAnchor(
                    coarseLevel
                );

            stitchRenderer.GetPropertyBlock(
                transitionPropertyBlock
            );

            transitionPropertyBlock.SetVector(
                ClipmapTransitionOffsetPropertyId,
                new Vector4(
                    fineMinusCoarse.x,
                    0f,
                    fineMinusCoarse.z,
                    0f
                )
            );

            stitchRenderer.SetPropertyBlock(
                transitionPropertyBlock
            );

            /*
             * The shader can move transition vertices horizontally
             * after the normal object-to-world transform.
             *
             * Expand the renderer's X/Z local bounds so Unity's
             * CPU frustum culling cannot reject a stitch whose
             * displaced vertices have moved outside the generated
             * source mesh bounds.
             *
             * Y is deliberately preserved because
             * TerrainClipmapBoundsController owns displaced terrain
             * height bounds.
             */
            ApplyStitchHorizontalBounds(
                stitchRenderer,
                fineMinusCoarse
            );
        }

        return true;
    }

    // =====================================================
    // RESET GENERATED LOD GEOMETRY
    // =====================================================

    /*
     * Mirrors the old TerrainClipmapController reset behavior:
     *
     * - keep the current clipmap root position
     * - reset coarse LOD child local positions
     * - reset stitch transition offsets
     *
     * This is used during runtime shutdown and will also be useful
     * when editor-following code temporarily restores canonical
     * hierarchy state.
     */
    public bool TryReset(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (
            clipmapRoot == null
            ||
            worldSettings == null
        )
        {
            return true;
        }

        if (
            !TryEnsureHierarchyReferences(
                out errorMessage
            )
        )
        {
            return false;
        }

        EnsureTransitionPropertyBlock();

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            Transform levelTransform =
                lodLevelTransforms[
                    level
                ];

            if (levelTransform != null)
            {
                levelTransform.localPosition =
                    Vector3.zero;
            }

            MeshRenderer stitchRenderer =
                stitchRenderers[
                    level
                ];

            if (stitchRenderer == null)
            {
                continue;
            }

            stitchRenderer.GetPropertyBlock(
                transitionPropertyBlock
            );

            transitionPropertyBlock.SetVector(
                ClipmapTransitionOffsetPropertyId,
                Vector4.zero
            );

            stitchRenderer.SetPropertyBlock(
                transitionPropertyBlock
            );

            ApplyStitchHorizontalBounds(
                stitchRenderer,
                Vector3.zero
            );
        }

        return true;
    }

    // =====================================================
    // HIERARCHY ACCESS FOR DIAGNOSTICS
    // =====================================================

    public bool TryGetLODTransform(
        int level,
        out Transform lodTransform,
        out string errorMessage
    )
    {
        lodTransform =
            null;

        errorMessage =
            "";

        if (
            clipmapRoot == null
            ||
            worldSettings == null
        )
        {
            errorMessage =
                "Clipmap layout applier is not configured.";

            return false;
        }

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        if (
            level < 0
            ||
            level >= levelCount
        )
        {
            errorMessage =
                $"LOD level {level} is outside the configured range.";

            return false;
        }

        if (level == 0)
        {
            lodTransform =
                clipmapRoot;

            return true;
        }

        if (
            !TryEnsureHierarchyReferences(
                out errorMessage
            )
        )
        {
            return false;
        }

        lodTransform =
            lodLevelTransforms[
                level
            ];

        return
            lodTransform != null;
    }

    public bool TryGetStitchRenderer(
        int coarseLevel,
        out MeshRenderer stitchRenderer,
        out string errorMessage
    )
    {
        stitchRenderer =
            null;

        errorMessage =
            "";

        if (
            !TryEnsureHierarchyReferences(
                out errorMessage
            )
        )
        {
            return false;
        }

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        if (
            coarseLevel <= 0
            ||
            coarseLevel >= levelCount
        )
        {
            errorMessage =
                $"Stitch coarse level {coarseLevel} is outside " +
                "the configured range.";

            return false;
        }

        stitchRenderer =
            stitchRenderers[
                coarseLevel
            ];

        return
            stitchRenderer != null;
    }

    public bool TryGetStitchTransitionOffset(
        int coarseLevel,
        out Vector4 transitionOffset,
        out string errorMessage
    )
    {
        transitionOffset =
            Vector4.zero;

        if (
            !TryGetStitchRenderer(
                coarseLevel,
                out MeshRenderer stitchRenderer,
                out errorMessage
            )
        )
        {
            return false;
        }

        EnsureTransitionPropertyBlock();

        stitchRenderer.GetPropertyBlock(
            transitionPropertyBlock
        );

        transitionOffset =
            transitionPropertyBlock.GetVector(
                ClipmapTransitionOffsetPropertyId
            );

        return true;
    }

    // =====================================================
    // INVALIDATE HIERARCHY REFERENCES
    // =====================================================

    public void InvalidateHierarchyReferences()
    {
        hierarchyReferencesValid =
            false;

        lodLevelTransforms =
            null;

        stitchRenderers =
            null;
    }

    // =====================================================
    // VALIDATE CONFIGURATION
    // =====================================================

    private bool TryValidateConfiguration(
        TerrainClipmapLayout layout,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (clipmapRoot == null)
        {
            errorMessage =
                "Clipmap root is null.";

            return false;
        }

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (
            layout == null
            ||
            !layout.IsValid
        )
        {
            errorMessage =
                "TerrainClipmapLayout is unavailable or invalid.";

            return false;
        }

        int expectedLevelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        if (
            layout.LevelCount !=
            expectedLevelCount
        )
        {
            errorMessage =
                "TerrainClipmapLayout level count does not match " +
                "the configured WorldSettings.\n\n" +
                $"Expected: {expectedLevelCount}\n" +
                $"Actual: {layout.LevelCount}";

            return false;
        }

        return true;
    }

    // =====================================================
    // ENSURE GENERATED HIERARCHY REFERENCES
    // =====================================================

    private bool TryEnsureHierarchyReferences(
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (clipmapRoot == null)
        {
            errorMessage =
                "Clipmap root is null.";

            return false;
        }

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        int levelCount =
            TerrainClipmapLayoutUtility
                .GetLevelCount(
                    worldSettings
                );

        bool storageMatches =
            lodLevelTransforms != null
            &&
            stitchRenderers != null
            &&
            lodLevelTransforms.Length ==
                levelCount
            &&
            stitchRenderers.Length ==
                levelCount;

        if (
            hierarchyReferencesValid
            &&
            storageMatches
        )
        {
            bool referencesStillValid =
                true;

            for (
                int level = 1;
                level < levelCount;
                level++
            )
            {
                if (
                    lodLevelTransforms[
                        level
                    ] == null
                    ||
                    stitchRenderers[
                        level
                    ] == null
                )
                {
                    referencesStillValid =
                        false;

                    break;
                }
            }

            if (referencesStillValid)
            {
                return true;
            }
        }

        lodLevelTransforms =
            new Transform[
                levelCount
            ];

        stitchRenderers =
            new MeshRenderer[
                levelCount
            ];

        for (
            int level = 1;
            level < levelCount;
            level++
        )
        {
            string levelName =
                $"LOD{level}";

            Transform levelTransform =
                clipmapRoot.Find(
                    levelName
                );

            if (levelTransform == null)
            {
                errorMessage =
                    $"Missing generated clipmap group '{levelName}'.";

                hierarchyReferencesValid =
                    false;

                return false;
            }

            string stitchName =
                $"Stitch_LOD{level - 1}_LOD{level}";

            Transform stitchTransform =
                levelTransform.Find(
                    stitchName
                );

            if (stitchTransform == null)
            {
                errorMessage =
                    $"Missing generated stitch '{stitchName}' " +
                    $"under '{levelName}'.";

                hierarchyReferencesValid =
                    false;

                return false;
            }

            MeshRenderer stitchRenderer =
                stitchTransform
                    .GetComponent<MeshRenderer>();

            if (stitchRenderer == null)
            {
                errorMessage =
                    $"Generated stitch '{stitchName}' has no " +
                    "MeshRenderer.";

                hierarchyReferencesValid =
                    false;

                return false;
            }

            lodLevelTransforms[
                level
            ] =
                levelTransform;

            stitchRenderers[
                level
            ] =
                stitchRenderer;
        }

        hierarchyReferencesValid =
            true;

        return true;
    }

    // =====================================================
    // STITCH HORIZONTAL BOUNDS
    // =====================================================

    private static void ApplyStitchHorizontalBounds(
        MeshRenderer stitchRenderer,
        Vector3 worldTransitionOffset
    )
    {
        if (stitchRenderer == null)
        {
            return;
        }

        MeshFilter meshFilter =
            stitchRenderer
                .GetComponent<MeshFilter>();

        if (
            meshFilter == null
            ||
            meshFilter.sharedMesh == null
        )
        {
            return;
        }

        Bounds sourceBounds =
            meshFilter.sharedMesh.bounds;

        Bounds currentBounds =
            stitchRenderer.localBounds;

        /*
         * _ClipmapTransitionOffset is a world-space displacement.
         * Convert it to the stitch renderer's local vector space
         * before expanding local renderer bounds.
         */
        Vector3 localOffset =
            stitchRenderer.transform
                .InverseTransformVector(
                    new Vector3(
                        worldTransitionOffset.x,
                        0f,
                        worldTransitionOffset.z
                    )
                );

        float minimumX =
            Mathf.Min(
                sourceBounds.min.x,
                sourceBounds.min.x +
                    localOffset.x
            );

        float maximumX =
            Mathf.Max(
                sourceBounds.max.x,
                sourceBounds.max.x +
                    localOffset.x
            );

        float minimumZ =
            Mathf.Min(
                sourceBounds.min.z,
                sourceBounds.min.z +
                    localOffset.z
            );

        float maximumZ =
            Mathf.Max(
                sourceBounds.max.z,
                sourceBounds.max.z +
                    localOffset.z
            );

        /*
         * Preserve the current Y range. TerrainClipmapBoundsController
         * may already have expanded it for GPU height displacement.
         */
        float minimumY =
            currentBounds.min.y;

        float maximumY =
            currentBounds.max.y;

        Vector3 minimum =
            new Vector3(
                minimumX,
                minimumY,
                minimumZ
            );

        Vector3 maximum =
            new Vector3(
                maximumX,
                maximumY,
                maximumZ
            );

        Bounds expandedBounds =
            new Bounds();

        expandedBounds.SetMinMax(
            minimum,
            maximum
        );

        stitchRenderer.localBounds =
            expandedBounds;
    }

    // =====================================================
    // PROPERTY BLOCK
    // =====================================================

    private void EnsureTransitionPropertyBlock()
    {
        if (transitionPropertyBlock != null)
        {
            return;
        }

        transitionPropertyBlock =
            new MaterialPropertyBlock();
    }
}
