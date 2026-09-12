/*
 * Persistent interpolation-mode identity for node-based regional elevation.
 *
 * CPU and GPU capability are reported separately. Package I3 enables CPU
 * Triangulated Linear evaluation while GPU terrain composition remains IDW-only
 * until Package I4.
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
        return mode ==
            TerrainNodeElevationInterpolationMode.InverseDistanceWeighted;
    }

    /*
     * Backward-compatible production-readiness query retained for existing
     * callers. A mode is fully implemented only when the current GPU terrain
     * composition pipeline can render it.
     */
    public static bool IsImplemented(
        TerrainNodeElevationInterpolationMode mode)
    {
        return SupportsGpuComposition(mode);
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

        if (
            mode ==
            TerrainNodeElevationInterpolationMode.TriangulatedLinear
        )
        {
            return
                "Triangulated Linear CPU evaluation is implemented, but GPU " +
                "regional terrain composition is not available until Package I4.";
        }

        return
            "Regional elevation interpolation mode '" +
            GetDisplayName(mode) +
            "' does not have GPU regional terrain composition support yet.";
    }

    /*
     * Backward-compatible message for callers that require full production
     * composition support rather than CPU evaluation alone.
     */
    public static string GetNotImplementedMessage(
        TerrainNodeElevationInterpolationMode mode)
    {
        if (!SupportsCpuEvaluation(mode))
        {
            return GetCpuNotImplementedMessage(mode);
        }

        return GetGpuNotImplementedMessage(mode);
    }
}
