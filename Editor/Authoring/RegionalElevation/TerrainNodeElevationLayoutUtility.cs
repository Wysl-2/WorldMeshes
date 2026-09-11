using UnityEngine;

/*
 * Package 2 setup-time layout generation for node-based regional elevation.
 *
 * The grid is only a creation aid. Generated TerrainElevationNode instances
 * persist only their normal Package 1 data (stable ID, XZ position, elevation)
 * and have no continuing relationship to rows, columns, spacing, or divisions.
 */
public static class TerrainNodeElevationLayoutUtility
{
    /*
     * Regional elevation is intended to use a relatively small number of broad
     * control nodes. This guard is intentionally generous and exists only to
     * prevent accidental setup-time allocations from extreme division input.
     */
    public const int MaximumGeneratedNodeCount =
        10000;

    public static bool TryGetGeneratedNodeCounts(
        int divisionsX,
        int divisionsZ,
        out int nodeCountX,
        out int nodeCountZ,
        out int totalNodeCount,
        out string errorMessage
    )
    {
        nodeCountX =
            0;

        nodeCountZ =
            0;

        totalNodeCount =
            0;

        errorMessage =
            "";

        if (
            divisionsX < 1
            ||
            divisionsZ < 1
        )
        {
            errorMessage =
                "Regional elevation grid divisions must be at least 1 " +
                "on both axes.";

            return false;
        }

        long nodeCountXLong =
            (long)divisionsX +
            1L;

        long nodeCountZLong =
            (long)divisionsZ +
            1L;

        long totalNodeCountLong =
            nodeCountXLong *
            nodeCountZLong;

        if (
            nodeCountXLong > int.MaxValue
            ||
            nodeCountZLong > int.MaxValue
            ||
            totalNodeCountLong > int.MaxValue
        )
        {
            errorMessage =
                "Regional elevation grid dimensions are too large.";

            return false;
        }

        if (
            totalNodeCountLong >
                MaximumGeneratedNodeCount
        )
        {
            errorMessage =
                $"The requested layout would generate " +
                $"{totalNodeCountLong:N0} nodes. Package 2 limits one " +
                $"initial layout to {MaximumGeneratedNodeCount:N0} nodes " +
                "to prevent accidental excessive editor allocations.";

            return false;
        }

        nodeCountX =
            (int)nodeCountXLong;

        nodeCountZ =
            (int)nodeCountZLong;

        totalNodeCount =
            (int)totalNodeCountLong;

        return true;
    }

    public static bool TryCreateFourCornerSource(
        WorldSettings worldSettings,
        float initialElevation,
        out TerrainNodeElevationSource source,
        out string errorMessage
    )
    {
        return
            TryCreateGridSource(
                worldSettings,
                1,
                1,
                initialElevation,
                out source,
                out errorMessage
            );
    }

    public static bool TryCreateGridSource(
        WorldSettings worldSettings,
        int divisionsX,
        int divisionsZ,
        float initialElevation,
        out TerrainNodeElevationSource source,
        out string errorMessage
    )
    {
        source =
            null;

        errorMessage =
            "";

        if (worldSettings == null)
        {
            errorMessage =
                "WorldSettings is null.";

            return false;
        }

        if (!IsFinite(
            initialElevation
        ))
        {
            errorMessage =
                "Initial node elevation must be finite.";

            return false;
        }

        if (
            !TryGetGeneratedNodeCounts(
                divisionsX,
                divisionsZ,
                out _,
                out _,
                out int totalNodeCount,
                out errorMessage
            )
        )
        {
            return false;
        }

        Vector2 worldSizeXZ =
            TerrainClipmapLayoutUtility
                .CalculateWorldSizeXZ(
                    worldSettings
                );

        if (
            !IsFinite(
                worldSizeXZ.x
            )
            ||
            !IsFinite(
                worldSizeXZ.y
            )
            ||
            worldSizeXZ.x <= 0f
            ||
            worldSizeXZ.y <= 0f
        )
        {
            errorMessage =
                "WorldSettings produced an invalid logical world size.";

            return false;
        }

        TerrainNodeElevationSource generatedSource =
            new TerrainNodeElevationSource();

        /*
         * Deterministic ordering is row-major in Z, then X:
         *
         * z = 0, x = 0..divisionsX
         * z = 1, x = 0..divisionsX
         * ...
         * z = divisionsZ
         *
         * For the 1 x 1 Four Corners preset this produces:
         * southwest, southeast, northwest, northeast.
         */
        for (
            int z = 0;
            z <= divisionsZ;
            z++
        )
        {
            float normalizedZ =
                (float)z /
                divisionsZ;

            float positionZ =
                z == 0
                    ? 0f
                    :
                    z == divisionsZ
                        ? worldSizeXZ.y
                        : normalizedZ * worldSizeXZ.y;

            for (
                int x = 0;
                x <= divisionsX;
                x++
            )
            {
                float normalizedX =
                    (float)x /
                    divisionsX;

                float positionX =
                    x == 0
                        ? 0f
                        :
                        x == divisionsX
                            ? worldSizeXZ.x
                            : normalizedX * worldSizeXZ.x;

                TerrainElevationNode node =
                    new TerrainElevationNode();

                node.SetPositionXZInternal(
                    new Vector2(
                        positionX,
                        positionZ
                    )
                );

                node.SetElevationInternal(
                    initialElevation
                );

                generatedSource.AddNodeInternal(
                    node
                );
            }
        }

        if (
            generatedSource.NodeCount !=
                totalNodeCount
        )
        {
            errorMessage =
                "Regional elevation node generation produced an " +
                "unexpected node count.";

            return false;
        }

        generatedSource.RepairNodeStableIds();

        if (
            !generatedSource.TryValidateNodeStableIds(
                out string identityError
            )
        )
        {
            errorMessage =
                "Generated elevation-node identities are invalid. " +
                identityError;

            return false;
        }

        if (
            !generatedSource.TryValidateOutputData(
                out string outputError
            )
        )
        {
            errorMessage =
                "Generated elevation-node output data is invalid. " +
                outputError;

            return false;
        }

        source =
            generatedSource;

        return true;
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
