using UnityEditor.AddressableAssets.Settings.GroupSchemas;

/*
 * Physical packing policy for the multiresolution Height Addressables group.
 * Runtime code must consume logical (stride,x,z) addresses and remain
 * independent from this policy. MRH04 intentionally preserves the existing
 * PackSeparately topology for measurement before any packing change is made.
 */
internal static class TerrainHeightAddressablesPackingPolicy
{
    internal static BundledAssetGroupSchema.BundlePackingMode
        ExpectedBundleMode =>
            BundledAssetGroupSchema
                .BundlePackingMode
                .PackSeparately;

    internal static int EstimateHeightBundleCount(
        int heightEntryCount,
        int representationLevelCount
    )
    {
        switch (ExpectedBundleMode)
        {
            case BundledAssetGroupSchema.BundlePackingMode.PackTogether:
                return heightEntryCount > 0 ? 1 : 0;

            case BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel:
                return representationLevelCount;

            default:
                return heightEntryCount;
        }
    }
}
