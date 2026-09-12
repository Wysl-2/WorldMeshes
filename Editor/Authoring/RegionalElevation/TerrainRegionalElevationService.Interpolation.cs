public static partial class TerrainRegionalElevationService
{
    /*
     * Persistent interpolation-mode mutation boundary.
     *
     * All known planned enum values may be stored so signature/snapshot/Undo
     * behavior is forward-compatible with later interpolation packages.
     * Production evaluators and the WorldMeshes UI still expose IDW only until
     * those later modes are implemented.
     */
    public static bool SetInterpolationMode(
        TerrainAuthoringData authoringData,
        WorldSettings worldSettings,
        TerrainNodeElevationInterpolationMode interpolationMode,
        out string errorMessage)
    {
        errorMessage = "";

        if (!TerrainNodeElevationInterpolationModeUtility.IsKnown(
            interpolationMode))
        {
            errorMessage =
                "Requested regional elevation interpolation mode is invalid.";
            return false;
        }

        if (HasActiveInteractiveEdit)
        {
            errorMessage =
                "A regional elevation interactive edit is currently active.";
            return false;
        }

        if (!TryGetValidatedNodeSource(
            authoringData,
            out TerrainNodeElevationSource source,
            out errorMessage))
        {
            return false;
        }

        if (worldSettings == null)
        {
            errorMessage = "WorldSettings is null.";
            return false;
        }

        if (source.InterpolationMode == interpolationMode)
        {
            SetNoChangeDiagnostics(
                "Set Regional Elevation Interpolation Mode",
                authoringData,
                worldSettings);
            return true;
        }

        return ExecuteMutation(
            authoringData,
            worldSettings,
            "Set Regional Elevation Interpolation Mode",
            () =>
            {
                source.SetInterpolationModeInternal(interpolationMode);
                return true;
            },
            out errorMessage);
    }
}
