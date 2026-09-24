using System;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

/*
 * Physical packing policy for generated Surface Mask Addressables.
 * Logical tile addresses remain unchanged; ARH02 groups nearby tiles into
 * deterministic spatial bundles through one tool-owned region label.
 */
internal static class TerrainSurfaceAddressablesPackingPolicy
{
    internal const int RegionTileSpan =
        4;

    internal const string RegionLabelPrefix =
        "TerrainSurface_Region_";

    internal static BundledAssetGroupSchema.BundlePackingMode
        ExpectedBundleMode =>
            BundledAssetGroupSchema
                .BundlePackingMode
                .PackTogetherByLabel;

    internal static int GetRegionGridWidth(
        int tileGridWidth
    )
    {
        return
            GetRegionGridSize(
                tileGridWidth
            );
    }

    internal static int GetRegionGridHeight(
        int tileGridHeight
    )
    {
        return
            GetRegionGridSize(
                tileGridHeight
            );
    }

    internal static string GetRegionLabel(
        int tileX,
        int tileZ
    )
    {
        int safeSpan =
            Math.Max(
                1,
                RegionTileSpan
            );

        int regionX =
            Math.Max(
                0,
                tileX
            )
            /
            safeSpan;

        int regionZ =
            Math.Max(
                0,
                tileZ
            )
            /
            safeSpan;

        return
            RegionLabelPrefix +
            regionX +
            "_" +
            regionZ;
    }

    internal static bool IsManagedRegionLabel(
        string label
    )
    {
        return
            !string.IsNullOrEmpty(label)
            &&
            label.StartsWith(
                RegionLabelPrefix,
                StringComparison.Ordinal
            );
    }

    internal static long EstimateSurfaceBundleCount(
        int tileGridWidth,
        int tileGridHeight
    )
    {
        return
            (long)GetRegionGridWidth(
                tileGridWidth
            )
            *
            GetRegionGridHeight(
                tileGridHeight
            );
    }

    private static int GetRegionGridSize(
        int tileCount
    )
    {
        long safeTileCount =
            Math.Max(
                1,
                tileCount
            );

        long safeSpan =
            Math.Max(
                1,
                RegionTileSpan
            );

        long regionCount =
            (
                safeTileCount +
                safeSpan -
                1L
            )
            /
            safeSpan;

        return
            regionCount > int.MaxValue
                ? int.MaxValue
                : (int)regionCount;
    }
}
