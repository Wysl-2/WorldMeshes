using UnityEngine;

public static partial class TerrainAuthoringModifierService
{
    /*
     * Production creation path for a library stamp definition.
     *
     * Asset defaults are copied exactly once into the new modifier. No live
     * relationship exists between the asset defaults and the placed modifier
     * after this method returns.
     */
    public static bool AddStampModifierFromAssetDefaults(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainHeightStampAsset stampAsset,
        Vector2 positionXZ,
        out string stableId,
        out string errorMessage
    )
    {
        stableId =
            "";

        errorMessage =
            "";

        if (stampAsset == null)
        {
            errorMessage =
                "A TerrainHeightStampAsset is required when creating a stamp from asset defaults.";

            return false;
        }

        TerrainStampModifier newModifier =
            new TerrainStampModifier();

        newModifier.SetStampAssetInternal(
            stampAsset
        );

        newModifier.SetPositionXZInternal(
            positionXZ
        );

        newModifier.SetSizeXZInternal(
            stampAsset.DefaultSizeXZ
        );

        newModifier.SetRotationDegreesInternal(
            0f
        );

        newModifier.SetFlipXInternal(
            false
        );

        newModifier.SetFlipZInternal(
            false
        );

        newModifier.SetSourceRemapInternal(
            stampAsset.DefaultSourceInputMin,
            stampAsset.DefaultSourceInputMax,
            stampAsset.DefaultSourceGamma
        );

        newModifier.SetHeightDeltaInternal(
            stampAsset.DefaultHeightDelta
        );

        newModifier.SetTargetBaseHeightInternal(
            stampAsset.DefaultTargetBaseHeight
        );

        newModifier.SetTargetHeightRangeInternal(
            stampAsset.DefaultTargetHeightRange
        );

        newModifier.SetFalloffShapeInternal(
            stampAsset.DefaultFalloffShape
        );

        newModifier.SetFalloffProfileInternal(
            stampAsset.DefaultFalloffProfile
        );

        newModifier.SetFalloffInternal(
            stampAsset.DefaultFalloff
        );

        newModifier.SetSmoothingRadiusInternal(
            stampAsset.DefaultSmoothingRadius
        );

        newModifier.SetSmoothingStrengthInternal(
            stampAsset.DefaultSmoothingStrength
        );

        newModifier.SetBlendModeInternal(
            stampAsset.DefaultBlendMode
        );

        newModifier.SetEnabledInternal(
            true
        );

        if (
            !ExecuteMutation(
                authoringData,
                worldSettings,
                "Add Terrain Height Modifier",
                () =>
                {
                    authoringData
                        .AddHeightModifierInternal(
                            newModifier
                        );

                    return true;
                },
                out errorMessage
            )
        )
        {
            return false;
        }

        stableId =
            newModifier.StableId;

        return
            !string.IsNullOrEmpty(
                stableId
            );
    }
}
