/*
 * Persistent interpolation-mode identity for node-based regional elevation.
 *
 * CPU and GPU capability are reported separately. Package I4 completes GPU
 * Triangulated Linear composition while Triangulated Smooth remains deferred.
 */
public enum TerrainNodeElevationInterpolationMode
{
    InverseDistanceWeighted = 0,
    TriangulatedLinear = 1,
    TriangulatedSmooth = 2
}

public static class TerrainNodeElevationInterpolationModeUtility
{
    public static bool IsKnown(
        TerrainNodeElevationInterpolationMode mode)
    {
        switch (mode)
        {
            case TerrainNodeElevationInterpolationMode.InverseDistanceWeighted:
            case TerrainNodeElevationInterpolationMode.TriangulatedLinear:
            case TerrainNodeElevationInterpolationMode.TriangulatedSmooth:
                return true;

            default:
                return false;
        }
    }

    public static bool SupportsCpuEvaluation(
        TerrainNodeElevationInterpolationMode mode)
    {
        switch (mode)
        {
            case TerrainNodeElevationInterpolationMode.InverseDistanceWeighted:
            case TerrainNodeElevationInterpolationMode.TriangulatedLinear:
                return true;

            default:
                return false;
        }
    }

    public static bool SupportsGpuComposition(
        TerrainNodeElevationInterpolationMode mode)
    {
        switch (mode)
        {
            case TerrainNodeElevationInterpolationMode.InverseDistanceWeighted:
            case TerrainNodeElevationInterpolationMode.TriangulatedLinear:
                return true;

            default:
                return false;
        }
    }

    /*
     * Backward-compatible production-readiness query retained for existing
     * callers. A mode is fully implemented only when both CPU evaluation and
     * the current GPU terrain-composition pipeline support it.
     */
    public static bool IsImplemented(
        TerrainNodeElevationInterpolationMode mode)
    {
        return
            SupportsCpuEvaluation(mode) &&
            SupportsGpuComposition(mode);
    }

    public static string GetDisplayName(
        TerrainNodeElevationInterpolationMode mode)
    {
        switch (mode)
        {
            case TerrainNodeElevationInterpolationMode.InverseDistanceWeighted:
                return "Inverse Distance Weighted";

            case TerrainNodeElevationInterpolationMode.TriangulatedLinear:
                return "Triangulated Linear";

            case TerrainNodeElevationInterpolationMode.TriangulatedSmooth:
                return "Triangulated Smooth";

            default:
                return "Invalid (" + ((int)mode).ToString() + ")";
        }
    }

    public static string GetCpuNotImplementedMessage(
        TerrainNodeElevationInterpolationMode mode)
    {
        if (!IsKnown(mode))
        {
            return
                "Regional elevation interpolation mode '" +
                GetDisplayName(mode) +
                "' is invalid.";
        }

        return
            "Regional elevation interpolation mode '" +
            GetDisplayName(mode) +
            "' does not have CPU evaluation support yet.";
    }

    public static string GetGpuNotImplementedMessage(
        TerrainNodeElevationInterpolationMode mode)
    {
        if (!IsKnown(mode))
        {
            return
                "Regional elevation interpolation mode '" +
                GetDisplayName(mode) +
                "' is invalid.";
        }

        return
            "Regional elevation interpolation mode '" +
            GetDisplayName(mode) +
            "' does not have GPU regional terrain composition support yet.";
    }

    /*
     * Backward-compatible message for callers that require complete production
     * support rather than one specific capability.
     */
    public static string GetNotImplementedMessage(
        TerrainNodeElevationInterpolationMode mode)
    {
        if (!SupportsCpuEvaluation(mode))
        {
            return GetCpuNotImplementedMessage(mode);
        }

        if (!SupportsGpuComposition(mode))
        {
            return GetGpuNotImplementedMessage(mode);
        }

        return
            "Regional elevation interpolation mode '" +
            GetDisplayName(mode) +
            "' is implemented.";
    }
}
