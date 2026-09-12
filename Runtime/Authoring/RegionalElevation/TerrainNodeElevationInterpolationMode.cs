/*
 * Persistent interpolation-mode identity for node-based regional elevation.
 *
 * Package I1 establishes the source-level mode contract only. IDW remains the
 * only production evaluator/compositor in this package; later interpolation
 * packages activate the triangulated modes without changing serialized values.
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

    public static bool IsImplemented(
        TerrainNodeElevationInterpolationMode mode)
    {
        return mode ==
            TerrainNodeElevationInterpolationMode.InverseDistanceWeighted;
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

    public static string GetNotImplementedMessage(
        TerrainNodeElevationInterpolationMode mode)
    {
        return
            "Regional elevation interpolation mode '" +
            GetDisplayName(mode) +
            "' is not implemented yet. " +
            "Package I1 supports production evaluation/composition through " +
            "Inverse Distance Weighted only.";
    }
}
