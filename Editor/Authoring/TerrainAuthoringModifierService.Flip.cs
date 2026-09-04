public static partial class TerrainAuthoringModifierService
{
    // =====================================================
    // STAMP SOURCE ORIENTATION
    // =====================================================

    public static bool SetStampFlipX(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        bool flipX,
        out string errorMessage
    )
    {
        if (
            !TryFindStampModifier(
                authoringData,
                stableId,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (modifier.FlipX == flipX)
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Flip X",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Flip X",
                () =>
                {
                    modifier.SetFlipXInternal(
                        flipX
                    );

                    return true;
                },
                out errorMessage
            );
    }

    public static bool SetStampFlipZ(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        bool flipZ,
        out string errorMessage
    )
    {
        if (
            !TryFindStampModifier(
                authoringData,
                stableId,
                out TerrainStampModifier modifier,
                out errorMessage
            )
        )
        {
            return false;
        }

        if (modifier.FlipZ == flipZ)
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Flip Z",
                authoringData,
                worldSettings
            );

            return true;
        }

        return
            ExecuteMutation(
                authoringData,
                worldSettings,
                "Set Terrain Stamp Flip Z",
                () =>
                {
                    modifier.SetFlipZInternal(
                        flipZ
                    );

                    return true;
                },
                out errorMessage
            );
    }
}
