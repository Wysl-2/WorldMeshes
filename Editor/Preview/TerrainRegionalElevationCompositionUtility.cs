using System.Collections.Generic;
using System.Text;
using UnityEngine;

/*
 * Package 4 shared policy for regional-elevation composition.
 *
 * This utility owns only composition-time interpretation of the persistent
 * regional source and the logical height-tile set that must be recomposed.
 * It does not mutate authoring data, allocate GPU resources, or evaluate IDW.
 */
public static class TerrainRegionalElevationCompositionUtility
{
    public static bool TryResolveNodeSource(
        TerrainAuthoringData authoringData,
        out TerrainNodeElevationSource nodeSource,
        out bool regionalCompositionRequired,
        out string errorMessage
    )
    {
        nodeSource =
            null;

        regionalCompositionRequired =
            false;

        errorMessage =
            "";

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";

            return false;
        }

        TerrainRegionalElevationSource regionalSource =
            authoringData.RegionalElevationSource;

        if (regionalSource == null)
        {
            return true;
        }

        if (
            !(regionalSource is TerrainNodeElevationSource resolvedNodeSource)
        )
        {
            errorMessage =
                "Unsupported regional elevation source type: " +
                regionalSource.GetType().Name +
                ". Package 4 supports TerrainNodeElevationSource only.";

            return false;
        }

        if (
            authoringData.sourceMode !=
                TerrainHeightSourceMode.Flat
        )
        {
            errorMessage =
                "Node regional elevation composition currently supports only " +
                "a Flat committed heightfield source. " +
                $"Current source mode: {authoringData.sourceMode}.";

            return false;
        }

        if (
            !resolvedNodeSource.TryValidateOutputData(
                out string sourceError
            )
        )
        {
            errorMessage =
                "Regional elevation source output data is invalid. " +
                sourceError;

            return false;
        }

        if (
            resolvedNodeSource.NodeCount <=
            0
        )
        {
            errorMessage =
                "TerrainNodeElevationSource contains no elevation nodes and " +
                "does not define an evaluable regional surface.";

            return false;
        }

        nodeSource =
            resolvedNodeSource;

        regionalCompositionRequired =
            true;

        return true;
    }

    public static bool TryCollectRequiredHeightTiles(
        WorldSettings worldSettings,
        TerrainAuthoringData authoringData,
        ICollection<Vector2Int> output,
        int modifierSamplePadding,
        out bool regionalCompositionRequired,
        out string errorMessage
    )
    {
        regionalCompositionRequired =
            false;

        errorMessage =
            "";

        if (
            worldSettings == null
            ||
            authoringData == null
            ||
            output == null
        )
        {
            errorMessage =
                "Regional composition tile collection received an invalid " +
                "world, authoring data, or output collection.";

            return false;
        }

        if (
            worldSettings.HeightTileGridWidth <=
            0
            ||
            worldSettings.HeightTileGridHeight <=
            0
        )
        {
            errorMessage =
                "WorldSettings contains an invalid authoring height-tile grid.";

            return false;
        }

        if (
            !TryResolveNodeSource(
                authoringData,
                out _,
                out regionalCompositionRequired,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (regionalCompositionRequired)
        {
            CollectAllHeightTiles(
                worldSettings,
                output
            );
        }

        IReadOnlyList<TerrainHeightModifier> modifiers =
            authoringData.HeightModifiers;

        for (
            int modifierIndex = 0;
            modifierIndex < modifiers.Count;
            modifierIndex++
        )
        {
            TerrainHeightModifier modifier =
                modifiers[modifierIndex];

            if (modifier == null)
            {
                errorMessage =
                    $"Height modifier index {modifierIndex} is null.";

                return false;
            }

            if (!modifier.Enabled)
            {
                continue;
            }

            TerrainAuthoringPreviewDirtyRegionUtility
                .CollectTilesOverlappingBounds(
                    worldSettings,
                    modifier.GetAffectedWorldBounds(),
                    output,
                    modifierSamplePadding
                );
        }

        return true;
    }

    public static void CollectAllHeightTiles(
        WorldSettings worldSettings,
        ICollection<Vector2Int> output
    )
    {
        if (
            worldSettings == null
            ||
            output == null
        )
        {
            return;
        }

        int tileGridWidth =
            Mathf.Max(
                0,
                worldSettings.HeightTileGridWidth
            );

        int tileGridHeight =
            Mathf.Max(
                0,
                worldSettings.HeightTileGridHeight
            );

        for (
            int tileZ = 0;
            tileZ < tileGridHeight;
            tileZ++
        )
        {
            for (
                int tileX = 0;
                tileX < tileGridWidth;
                tileX++
            )
            {
                output.Add(
                    new Vector2Int(
                        tileX,
                        tileZ
                    )
                );
            }
        }
    }

    public static bool TryGetNodeElevationRange(
        TerrainNodeElevationSource nodeSource,
        out float minimumHeight,
        out float maximumHeight,
        out string errorMessage
    )
    {
        minimumHeight =
            0f;

        maximumHeight =
            0f;

        errorMessage =
            "";

        if (nodeSource == null)
        {
            errorMessage =
                "TerrainNodeElevationSource is null.";

            return false;
        }

        if (
            !nodeSource.TryValidateOutputData(
                out string sourceError
            )
        )
        {
            errorMessage =
                "Regional elevation source output data is invalid. " +
                sourceError;

            return false;
        }

        if (
            nodeSource.NodeCount <=
            0
        )
        {
            errorMessage =
                "TerrainNodeElevationSource contains no elevation nodes.";

            return false;
        }

        minimumHeight =
            float.PositiveInfinity;

        maximumHeight =
            float.NegativeInfinity;

        IReadOnlyList<TerrainElevationNode> nodes =
            nodeSource.Nodes;

        for (
            int index = 0;
            index < nodes.Count;
            index++
        )
        {
            float elevation =
                nodes[index].Elevation;

            minimumHeight =
                Mathf.Min(
                    minimumHeight,
                    elevation
                );

            maximumHeight =
                Mathf.Max(
                    maximumHeight,
                    elevation
                );
        }

        if (
            float.IsNaN(
                minimumHeight
            )
            ||
            float.IsInfinity(
                minimumHeight
            )
            ||
            float.IsNaN(
                maximumHeight
            )
            ||
            float.IsInfinity(
                maximumHeight
            )
            ||
            maximumHeight <
                minimumHeight
        )
        {
            errorMessage =
                "Regional elevation node range is invalid.";

            return false;
        }

        return true;
    }

    internal static bool TryGetRegionalOutputFingerprint(
        TerrainAuthoringData authoringData,
        out string fingerprint,
        out string errorMessage
    )
    {
        fingerprint =
            "";

        errorMessage =
            "";

        if (authoringData == null)
        {
            errorMessage =
                "TerrainAuthoringData is null.";

            return false;
        }

        TerrainRegionalElevationSource source =
            authoringData.RegionalElevationSource;

        if (source == null)
        {
            fingerprint =
                "<no-regional-source>";

            return true;
        }

        StringBuilder builder =
            new StringBuilder();

        if (
            !source.TryAppendDeterministicSignatureData(
                builder,
                out errorMessage
            )
        )
        {
            return false;
        }

        fingerprint =
            builder.ToString();

        return true;
    }
}
