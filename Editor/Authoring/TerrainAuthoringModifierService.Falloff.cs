public static partial class TerrainAuthoringModifierService
{
    public static bool SetStampFalloffShape(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        TerrainStampFalloffShape shape,
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

        TerrainStampFalloffShape safeShape =
            TerrainStampFalloffUtility
                .SanitizeShape(shape);

        if (modifier.FalloffShape == safeShape)
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Falloff Shape",
                authoringData,
                worldSettings
            );

            return true;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Set Terrain Stamp Falloff Shape",
            () =>
            {
                modifier.SetFalloffShapeInternal(
                    safeShape
                );

                return true;
            },
            out errorMessage
        );
    }

    public static bool SetStampFalloffProfile(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        string stableId,
        TerrainStampFalloffProfile profile,
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

        TerrainStampFalloffProfile safeProfile =
            TerrainStampFalloffUtility
                .SanitizeProfile(profile);

        if (modifier.FalloffProfile == safeProfile)
        {
            SetNoChangeDiagnostics(
                "Set Terrain Stamp Falloff Profile",
                authoringData,
                worldSettings
            );

            return true;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Set Terrain Stamp Falloff Profile",
            () =>
            {
                modifier.SetFalloffProfileInternal(
                    safeProfile
                );

                return true;
            },
            out errorMessage
        );
    }
}
