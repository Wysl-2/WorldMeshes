using UnityEngine;

/*
 * Immutable-by-convention result object populated by
 * TerrainClipmapLayoutUtility.
 *
 * The arrays are reused between calculations so runtime and
 * editor clipmap-following code can update layouts without
 * allocating every frame.
 */
public sealed class TerrainClipmapLayout
{
    private Vector3[] lodAnchors =
        new Vector3[0];

    private float[] lodSpacings =
        new float[0];

    public bool IsValid
    {
        get;
        internal set;
    }

    public int LevelCount
    {
        get;
        internal set;
    }

    public Vector2 MinimumXZ
    {
        get;
        internal set;
    }

    public Vector2 MaximumXZ
    {
        get;
        internal set;
    }

    public Vector3 CoverageCenter
    {
        get;
        internal set;
    }

    public float Diameter
    {
        get;
        internal set;
    }

    public bool TryGetLOD(
        int level,
        out Vector3 anchor,
        out float spacing
    )
    {
        anchor =
            Vector3.zero;

        spacing =
            0f;

        if (
            !IsValid
            ||
            level < 0
            ||
            level >= LevelCount
            ||
            level >= lodAnchors.Length
            ||
            level >= lodSpacings.Length
        )
        {
            return false;
        }

        anchor =
            lodAnchors[level];

        spacing =
            lodSpacings[level];

        return true;
    }

    public Vector3 GetAnchor(
        int level
    )
    {
        return
            lodAnchors[level];
    }

    public float GetSpacing(
        int level
    )
    {
        return
            lodSpacings[level];
    }

    internal void EnsureCapacity(
        int levelCount
    )
    {
        int safeLevelCount =
            Mathf.Max(
                1,
                levelCount
            );

        if (
            lodAnchors == null
            ||
            lodAnchors.Length !=
                safeLevelCount
        )
        {
            lodAnchors =
                new Vector3[
                    safeLevelCount
                ];
        }

        if (
            lodSpacings == null
            ||
            lodSpacings.Length !=
                safeLevelCount
        )
        {
            lodSpacings =
                new float[
                    safeLevelCount
                ];
        }
    }

    internal void SetLOD(
        int level,
        Vector3 anchor,
        float spacing
    )
    {
        lodAnchors[level] =
            anchor;

        lodSpacings[level] =
            spacing;
    }

    public void Invalidate()
    {
        IsValid =
            false;
    }
}

/*
 * Shared, side-effect-free clipmap placement mathematics.
 *
 * This class deliberately knows nothing about:
 *
 * - Player objects
 * - Scene View cameras
 * - Addressables / streaming
 * - GameObject hierarchy mutation
 * - MeshRenderers
 * - Play Mode / Edit Mode
 *
 * Runtime and editor systems provide a target position, receive
 * the same calculated layout, and decide separately when that
 * layout should be applied.
 */
public static class TerrainClipmapLayoutUtility
{
    public const int MinimumLevelCount =
        TerrainClipmapTopologyUtility.MinimumLevelCount;

    public const int MaximumLevelCount =
        TerrainClipmapTopologyUtility.MaximumLevelCount;

    // =====================================================
    // CALCULATE COMPLETE LAYOUT
    // =====================================================

    public static bool TryCalculateLayout(
        WorldSettings worldSettings,
        Vector3 targetWorldPosition,
        float fixedY,
        TerrainClipmapLayout layout,
        out string errorMessage
    )
    {
        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            if (layout != null)
            {
                layout.Invalidate();
            }

            return false;
        }

        if (layout == null)
        {
            errorMessage =
                "TerrainClipmapLayout is null.";

            return false;
        }

        if (
            !TerrainClipmapTopologyUtility
                .TryValidateSettings(
                    worldSettings,
                    out string topologyError
                )
        )
        {
            errorMessage =
                topologyError;

            layout.Invalidate();

            return false;
        }

        if (
            !IsFinite(
                targetWorldPosition.x
            )
            ||
            !IsFinite(
                targetWorldPosition.z
            )
            ||
            !IsFinite(
                fixedY
            )
        )
        {
            errorMessage =
                "The requested clipmap target position is invalid.";

            layout.Invalidate();

            return false;
        }

        int levelCount =
            GetLevelCount(
                worldSettings
            );

        layout.EnsureCapacity(
            levelCount
        );

        float minimumX =
            float.PositiveInfinity;

        float minimumZ =
            float.PositiveInfinity;

        float maximumX =
            float.NegativeInfinity;

        float maximumZ =
            float.NegativeInfinity;

        for (
            int level = 0;
            level < levelCount;
            level++
        )
        {
            float spacing =
                GetLODSpacing(
                    worldSettings,
                    level
                );

            Vector3 anchor =
                new Vector3(
                    SnapCoordinate(
                        targetWorldPosition.x,
                        spacing
                    ),

                    fixedY,

                    SnapCoordinate(
                        targetWorldPosition.z,
                        spacing
                    )
                );

            layout.SetLOD(
                level,
                anchor,
                spacing
            );

            /*
             * Each LOD now owns an independent outer grid
             * resolution while preserving the same power-of-two
             * spacing hierarchy.
             *
             * Bounds therefore come from the exact generated
             * topology for this level rather than assuming every
             * ring reuses the LOD0 resolution.
             */
            float halfExtent =
                TerrainClipmapTopologyUtility
                    .GetLODHalfExtent(
                        worldSettings,
                        level
                    );

            minimumX =
                Mathf.Min(
                    minimumX,
                    anchor.x -
                        halfExtent
                );

            maximumX =
                Mathf.Max(
                    maximumX,
                    anchor.x +
                        halfExtent
                );

            minimumZ =
                Mathf.Min(
                    minimumZ,
                    anchor.z -
                        halfExtent
                );

            maximumZ =
                Mathf.Max(
                    maximumZ,
                    anchor.z +
                        halfExtent
                );
        }

        layout.LevelCount =
            levelCount;

        layout.MinimumXZ =
            new Vector2(
                minimumX,
                minimumZ
            );

        layout.MaximumXZ =
            new Vector2(
                maximumX,
                maximumZ
            );

        /*
         * The outermost independently-snapped LOD anchor is the
         * center used by runtime height-cache coverage checks.
         *
         * Topology validation guarantees every finer LOD plus its
         * transition remains contained by the next coarser level,
         * so the outermost LOD remains an authoritative cache
         * footprint.
         */
        layout.CoverageCenter =
            layout.GetAnchor(
                levelCount - 1
            );

        layout.Diameter =
            CalculateClipmapDiameter(
                worldSettings
            );

        layout.IsValid =
            true;

        return true;
    }

    // =====================================================
    // LEVEL COUNT
    // =====================================================

    public static int GetLevelCount(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return
                MinimumLevelCount;
        }

        return
            Mathf.Clamp(
                worldSettings.clipmapLevelCount,
                MinimumLevelCount,
                MaximumLevelCount
            );
    }

    // =====================================================
    // LOD SPACING
    // =====================================================

    /*
     * Kept as the existing public layout API so runtime/editor
     * callers do not need to change. The authoritative spacing
     * calculation now lives with the shared topology rules.
     */
    public static float GetLODSpacing(
        WorldSettings worldSettings,
        int level
    )
    {
        return
            TerrainClipmapTopologyUtility
                .GetLODSpacing(
                    worldSettings,
                    level
                );
    }

    // =====================================================
    // CLIPMAP DIAMETER
    // =====================================================

    /*
     * Returns the exact outer coverage of the outermost LOD.
     *
     * TerrainHeightmapStreamer already calls this method for
     * cache sizing, so runtime height-cache coverage follows the
     * same per-LOD topology as generated geometry automatically.
     */
    public static float CalculateClipmapDiameter(
        WorldSettings worldSettings
    )
    {
        return
            TerrainClipmapTopologyUtility
                .CalculateClipmapDiameter(
                    worldSettings
                );
    }

    // =====================================================
    // SNAP COORDINATE
    // =====================================================

    public static float SnapCoordinate(
        float coordinate,
        float spacing
    )
    {
        float safeSpacing =
            Mathf.Max(
                0.0001f,
                spacing
            );

        return
            Mathf.Round(
                coordinate /
                safeSpacing
            ) *
            safeSpacing;
    }

    // =====================================================
    // VALID ADJACENT LOD OFFSET DISTANCE
    // =====================================================

    /*
     * With independently snapped power-of-two LOD grids, a fine
     * LOD center can differ from the adjacent coarse center by:
     *
     * -fineSpacing
     *  0
     * +fineSpacing
     *
     * along either horizontal axis.
     *
     * This helper remains useful for runtime/editor diagnostics.
     */
    public static float DistanceFromValidAdjacentOffset(
        float offset,
        float fineSpacing
    )
    {
        float negativeDifference =
            Mathf.Abs(
                offset +
                fineSpacing
            );

        float zeroDifference =
            Mathf.Abs(
                offset
            );

        float positiveDifference =
            Mathf.Abs(
                offset -
                fineSpacing
            );

        return
            Mathf.Min(
                negativeDifference,
                Mathf.Min(
                    zeroDifference,
                    positiveDifference
                )
            );
    }

    // =====================================================
    // WORLD SIZE
    // =====================================================

    /*
     * Single authoritative conversion from WorldSettings layout
     * values to the logical world size used by clipmap rendering
     * and editor authoring systems.
     */
    public static Vector2 CalculateWorldSizeXZ(
        WorldSettings worldSettings
    )
    {
        if (worldSettings == null)
        {
            return
                Vector2.zero;
        }

        float chunkSize =
            Mathf.Max(
                0.01f,
                worldSettings.chunkSize
            );

        return
            new Vector2(
                Mathf.Max(
                    1,
                    worldSettings.gridWidth
                ) *
                chunkSize,

                Mathf.Max(
                    1,
                    worldSettings.gridHeight
                ) *
                chunkSize
            );
    }

    // =====================================================
    // WORLD CENTER
    // =====================================================

    /*
     * Canonical generated clipmap position.
     *
     * TerrainWorldHierarchyGenerator and edit-mode preview systems
     * share this helper so canonical placement remains consistent.
     */
    public static Vector3 CalculateWorldCenterPosition(
        WorldSettings worldSettings,
        float y = 0f
    )
    {
        Vector2 worldSizeXZ =
            CalculateWorldSizeXZ(
                worldSettings
            );

        return
            new Vector3(
                worldSizeXZ.x *
                    0.5f,

                y,

                worldSizeXZ.y *
                    0.5f
            );
    }

    // =====================================================
    // CLAMP TARGET TO WORLD
    // =====================================================

    /*
     * Shared spatial helper for systems that intentionally keep a
     * clipmap follow target inside the logical terrain rectangle.
     */
    public static Vector3 ClampTargetXZToWorld(
        WorldSettings worldSettings,
        Vector3 targetWorldPosition
    )
    {
        Vector2 worldSize =
            CalculateWorldSizeXZ(
                worldSettings
            );

        if (
            worldSize.x <= 0f
            ||
            worldSize.y <= 0f
        )
        {
            return
                targetWorldPosition;
        }

        targetWorldPosition.x =
            Mathf.Clamp(
                targetWorldPosition.x,
                0f,
                worldSize.x
            );

        targetWorldPosition.z =
            Mathf.Clamp(
                targetWorldPosition.z,
                0f,
                worldSize.y
            );

        return
            targetWorldPosition;
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
}
