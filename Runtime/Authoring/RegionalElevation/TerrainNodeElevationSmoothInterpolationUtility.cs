using System;
using UnityEngine;

/*
 * Runtime-safe Package I6 CPU evaluator for Triangulated Smooth regional
 * elevation. Normal triangulated interiors evaluate the reduced-HCT patch field
 * produced by TerrainNodeElevationSmoothPatchUtility. Degenerate topology kinds
 * use explicit constant/projected cubic-Hermite rules.
 */
public static class TerrainNodeElevationSmoothInterpolationUtility
{
    private const double BarycentricTolerance = 1e-9;

    private static readonly double HullDistanceTieToleranceSquared =
        (double)TerrainNodeElevationGeometryUtility
            .MinimumTriangulationVertexSeparation *
        TerrainNodeElevationGeometryUtility
            .MinimumTriangulationVertexSeparation;

    private struct Double2
    {
        public double X;
        public double Z;

        public Double2(double x, double z)
        {
            X = x;
            Z = z;
        }

        public static Double2 operator +(Double2 a, Double2 b)
        {
            return new Double2(a.X + b.X, a.Z + b.Z);
        }

        public static Double2 operator -(Double2 a, Double2 b)
        {
            return new Double2(a.X - b.X, a.Z - b.Z);
        }

        public static Double2 operator *(Double2 a, double scalar)
        {
            return new Double2(a.X * scalar, a.Z * scalar);
        }
    }

    public static bool TryEvaluateHeight(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        TerrainNodeElevationSmoothPatchData patchData,
        Vector2 worldPositionXZ,
        out float height,
        out string errorMessage)
    {
        return TryEvaluateHeightAndGradient(
            source,
            topology,
            gradients,
            patchData,
            worldPositionXZ,
            out height,
            out _,
            out errorMessage);
    }

    internal static bool TryEvaluateHeightAndGradient(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        TerrainNodeElevationSmoothPatchData patchData,
        Vector2 worldPositionXZ,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        if (!TryValidateEvaluationInputs(
            source,
            topology,
            gradients,
            patchData,
            worldPositionXZ,
            out errorMessage))
        {
            return false;
        }

        if (topology.Kind == TerrainNodeElevationTopologyKind.Empty)
        {
            errorMessage =
                "TerrainNodeElevationSource contains no elevation nodes and " +
                "does not define an evaluable Smooth regional surface.";
            return false;
        }

        if (TryEvaluateExactNode(
            source,
            topology,
            gradients,
            worldPositionXZ,
            out bool matchedExactNode,
            out height,
            out gradientXZ,
            out errorMessage))
        {
            if (matchedExactNode)
            {
                return true;
            }
        }
        else
        {
            return false;
        }

        switch (topology.Kind)
        {
            case TerrainNodeElevationTopologyKind.SinglePoint:
                return TryEvaluateSingleNode(
                    source,
                    topology,
                    out height,
                    out gradientXZ,
                    out errorMessage);

            case TerrainNodeElevationTopologyKind.LineSegment:
                return TryEvaluateTwoNodeHermite(
                    source,
                    topology,
                    gradients,
                    worldPositionXZ,
                    out height,
                    out gradientXZ,
                    out errorMessage);

            case TerrainNodeElevationTopologyKind.Collinear:
                return TryEvaluateCollinearHermite(
                    source,
                    topology,
                    gradients,
                    worldPositionXZ,
                    out height,
                    out gradientXZ,
                    out errorMessage);

            case TerrainNodeElevationTopologyKind.Triangulated:
                if (topology.TryFindContainingTriangle(
                    worldPositionXZ,
                    out int triangleIndex))
                {
                    return TryEvaluateTrianglePatch(
                        topology,
                        patchData,
                        triangleIndex,
                        worldPositionXZ,
                        out height,
                        out gradientXZ,
                        out errorMessage);
                }

                return TryEvaluateHullExterior(
                    source,
                    topology,
                    gradients,
                    worldPositionXZ,
                    out height,
                    out gradientXZ,
                    out _,
                    out errorMessage);

            default:
                errorMessage =
                    "Regional topology kind is unsupported by Smooth CPU evaluation.";
                return false;
        }
    }

    /*
     * Validation/reference hook: independently evaluate one selected HCT
     * subpatch without the normal deterministic subpatch chooser. This allows
     * I6 validation to compare both sides of internal spokes and original
     * shared topology edges using production Bernstein evaluation.
     */
    internal static bool TryEvaluateTriangleSubpatchHeightAndGradient(
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationSmoothPatchData patchData,
        int triangleIndex,
        int subpatchIndex,
        Vector2 worldPositionXZ,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        if (
            topology == null ||
            patchData == null ||
            topology.Kind != TerrainNodeElevationTopologyKind.Triangulated ||
            patchData.PatchCount != topology.TriangleCount ||
            triangleIndex < 0 ||
            triangleIndex >= topology.TriangleCount ||
            subpatchIndex < 0 ||
            subpatchIndex > 2 ||
            !IsFinite(worldPositionXZ))
        {
            errorMessage =
                "Smooth direct subpatch evaluation received invalid input.";
            return false;
        }

        return TryEvaluateTrianglePatchSubpatch(
            topology,
            patchData,
            triangleIndex,
            subpatchIndex,
            worldPositionXZ,
            false,
            out height,
            out gradientXZ,
            out errorMessage);
    }

    internal static bool TryEvaluateHullExteriorForValidation(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        Vector2 worldPositionXZ,
        out float height,
        out Vector2 gradientXZ,
        out int selectedHullEdgeIndex,
        out string errorMessage)
    {
        return TryEvaluateHullExterior(
            source,
            topology,
            gradients,
            worldPositionXZ,
            out height,
            out gradientXZ,
            out selectedHullEdgeIndex,
            out errorMessage);
    }

    private static bool TryValidateEvaluationInputs(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        TerrainNodeElevationSmoothPatchData patchData,
        Vector2 worldPositionXZ,
        out string errorMessage)
    {
        errorMessage = "";

        if (source == null)
        {
            errorMessage = "TerrainNodeElevationSource is null.";
            return false;
        }

        if (topology == null)
        {
            errorMessage = "TerrainNodeElevationTopology is null.";
            return false;
        }

        if (gradients == null)
        {
            errorMessage = "TerrainNodeElevationGradientData is null.";
            return false;
        }

        if (patchData == null)
        {
            errorMessage = "TerrainNodeElevationSmoothPatchData is null.";
            return false;
        }

        if (!IsFinite(worldPositionXZ))
        {
            errorMessage =
                "Smooth regional elevation sample position contains a non-finite value.";
            return false;
        }

        if (!source.TryValidateOutputData(out string sourceError))
        {
            errorMessage =
                "Regional elevation source output data is invalid. " +
                sourceError;
            return false;
        }

        if (topology.VertexCount != source.NodeCount)
        {
            errorMessage =
                "Smooth regional topology is stale relative to the source node count.";
            return false;
        }

        if (gradients.GradientCount != topology.VertexCount)
        {
            errorMessage =
                "Smooth regional gradient count does not match topology vertices.";
            return false;
        }

        int expectedPatchCount =
            topology.Kind == TerrainNodeElevationTopologyKind.Triangulated
                ? topology.TriangleCount
                : 0;

        if (patchData.PatchCount != expectedPatchCount)
        {
            errorMessage =
                "Smooth patch count does not match the current topology kind.";
            return false;
        }

        for (int vertexIndex = 0;
            vertexIndex < topology.VertexCount;
            vertexIndex++)
        {
            TerrainNodeElevationTopologyVertex vertex =
                topology.Vertices[vertexIndex];

            if (
                vertex.SourceNodeIndex < 0 ||
                vertex.SourceNodeIndex >= source.NodeCount ||
                source.Nodes[vertex.SourceNodeIndex] == null)
            {
                errorMessage =
                    $"Smooth topology vertex {vertexIndex} has an invalid " +
                    "source mapping.";
                return false;
            }

            TerrainElevationNode node =
                source.Nodes[vertex.SourceNodeIndex];

            if (!node.TryGetStoredPositionXZInternal(
                out Vector2 storedPosition,
                out string positionError))
            {
                errorMessage =
                    $"Smooth topology vertex {vertexIndex} has invalid " +
                    "source geometry. " +
                    positionError;
                return false;
            }

            if (storedPosition != vertex.PositionXZ)
            {
                errorMessage =
                    $"Smooth topology vertex {vertexIndex} is stale relative " +
                    "to its source node.";
                return false;
            }

            if (!gradients.TryGetGradient(
                vertexIndex,
                out TerrainNodeElevationGradient gradient) ||
                !IsFinite(gradient.GradientXZ))
            {
                errorMessage =
                    $"Smooth topology vertex {vertexIndex} has an invalid I5 gradient.";
                return false;
            }
        }

        return true;
    }

    private static bool TryEvaluateExactNode(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        Vector2 point,
        out bool matched,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        matched = false;
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        for (int vertexIndex = 0;
            vertexIndex < topology.VertexCount;
            vertexIndex++)
        {
            TerrainNodeElevationTopologyVertex vertex =
                topology.Vertices[vertexIndex];

            if (point != vertex.PositionXZ)
            {
                continue;
            }

            int sourceNodeIndex =
                vertex.SourceNodeIndex;

            if (
                sourceNodeIndex < 0 ||
                sourceNodeIndex >= source.NodeCount ||
                source.Nodes[sourceNodeIndex] == null ||
                !gradients.TryGetGradient(
                    vertexIndex,
                    out TerrainNodeElevationGradient gradient))
            {
                errorMessage =
                    "Exact Smooth node sample could not resolve source/gradient data.";
                return false;
            }

            height =
                source.Nodes[sourceNodeIndex].Elevation;

            gradientXZ =
                gradient.GradientXZ;

            matched = true;
            return true;
        }

        return true;
    }

    private static bool TryEvaluateSingleNode(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        if (
            topology.VertexCount != 1 ||
            topology.Kind != TerrainNodeElevationTopologyKind.SinglePoint)
        {
            errorMessage =
                "Single-point Smooth topology is malformed.";
            return false;
        }

        int sourceNodeIndex =
            topology.Vertices[0].SourceNodeIndex;

        height =
            source.Nodes[sourceNodeIndex].Elevation;

        gradientXZ =
            Vector2.zero;

        return IsFinite(height);
    }

    private static bool TryEvaluateTwoNodeHermite(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        Vector2 point,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        if (
            topology.VertexCount != 2 ||
            topology.Kind != TerrainNodeElevationTopologyKind.LineSegment)
        {
            errorMessage =
                "Two-node Smooth topology is malformed.";
            return false;
        }

        return TryEvaluateProjectedHermiteInterval(
            source,
            topology,
            gradients,
            0,
            1,
            point,
            true,
            out height,
            out gradientXZ,
            out _,
            out errorMessage);
    }

    private static bool TryEvaluateCollinearHermite(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        Vector2 point,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        if (
            topology.Kind != TerrainNodeElevationTopologyKind.Collinear ||
            topology.VertexCount < 3)
        {
            errorMessage =
                "Collinear Smooth topology does not contain enough vertices.";
            return false;
        }

        Vector2 first =
            topology.Vertices[0].PositionXZ;

        Vector2 last =
            topology.Vertices[topology.VertexCount - 1].PositionXZ;

        double axisX =
            (double)last.x - first.x;

        double axisZ =
            (double)last.y - first.y;

        double axisLengthSquared =
            axisX * axisX +
            axisZ * axisZ;

        if (!IsUsableSegmentLength(axisLengthSquared))
        {
            errorMessage =
                "Collinear Smooth topology has an invalid common axis.";
            return false;
        }

        double sampleParameter =
            (((double)point.x - first.x) * axisX +
             ((double)point.y - first.y) * axisZ) /
            axisLengthSquared;

        if (!IsFinite(sampleParameter))
        {
            errorMessage =
                "Collinear Smooth projection produced a non-finite parameter.";
            return false;
        }

        if (sampleParameter <= 0.0)
        {
            return TryGetVertexHeight(
                source,
                topology,
                0,
                out height,
                out errorMessage);
        }

        if (sampleParameter >= 1.0)
        {
            return TryGetVertexHeight(
                source,
                topology,
                topology.VertexCount - 1,
                out height,
                out errorMessage);
        }

        double previousParameter = 0.0;

        for (int vertexIndex = 1;
            vertexIndex < topology.VertexCount;
            vertexIndex++)
        {
            Vector2 current =
                topology.Vertices[vertexIndex].PositionXZ;

            double currentParameter =
                (((double)current.x - first.x) * axisX +
                 ((double)current.y - first.y) * axisZ) /
                axisLengthSquared;

            if (
                !IsFinite(currentParameter) ||
                currentParameter <= previousParameter)
            {
                errorMessage =
                    "Collinear topology vertex order is inconsistent with its common axis.";
                return false;
            }

            if (sampleParameter <= currentParameter)
            {
                return TryEvaluateProjectedHermiteInterval(
                    source,
                    topology,
                    gradients,
                    vertexIndex - 1,
                    vertexIndex,
                    point,
                    false,
                    out height,
                    out gradientXZ,
                    out _,
                    out errorMessage);
            }

            previousParameter =
                currentParameter;
        }

        errorMessage =
            "Could not locate a collinear Smooth interpolation interval.";
        return false;
    }

    private static bool TryEvaluateProjectedHermiteInterval(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        int vertexIndexA,
        int vertexIndexB,
        Vector2 point,
        bool clampOutside,
        out float height,
        out Vector2 gradientXZ,
        out double clampedT,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        clampedT = 0.0;
        errorMessage = "";

        if (!TryGetHermiteEndpointData(
            source,
            topology,
            gradients,
            vertexIndexA,
            out Vector2 positionA,
            out double elevationA,
            out Double2 gradientA,
            out errorMessage) ||
            !TryGetHermiteEndpointData(
                source,
                topology,
                gradients,
                vertexIndexB,
                out Vector2 positionB,
                out double elevationB,
                out Double2 gradientB,
                out errorMessage))
        {
            return false;
        }

        double deltaX =
            (double)positionB.x - positionA.x;

        double deltaZ =
            (double)positionB.y - positionA.y;

        double lengthSquared =
            deltaX * deltaX +
            deltaZ * deltaZ;

        if (!IsUsableSegmentLength(lengthSquared))
        {
            errorMessage =
                "Smooth Hermite interval has an unsafe segment length.";
            return false;
        }

        double rawT =
            (((double)point.x - positionA.x) * deltaX +
             ((double)point.y - positionA.y) * deltaZ) /
            lengthSquared;

        if (!IsFinite(rawT))
        {
            errorMessage =
                "Smooth Hermite projection produced a non-finite parameter.";
            return false;
        }

        clampedT =
            Clamp01(rawT);

        if (clampOutside && rawT <= 0.0)
        {
            return TryConvertHeightAndGradient(
                elevationA,
                new Double2(0.0, 0.0),
                out height,
                out gradientXZ,
                out errorMessage);
        }

        if (clampOutside && rawT >= 1.0)
        {
            return TryConvertHeightAndGradient(
                elevationB,
                new Double2(0.0, 0.0),
                out height,
                out gradientXZ,
                out errorMessage);
        }

        Double2 delta =
            new Double2(deltaX, deltaZ);

        double mA =
            Dot(gradientA, delta);

        double mB =
            Dot(gradientB, delta);

        if (!TryEvaluateHermite(
            elevationA,
            elevationB,
            mA,
            mB,
            clampedT,
            out double evaluatedHeight,
            out double derivativeT,
            out errorMessage))
        {
            return false;
        }

        Double2 worldGradient =
            delta * (derivativeT / lengthSquared);

        return TryConvertHeightAndGradient(
            evaluatedHeight,
            worldGradient,
            out height,
            out gradientXZ,
            out errorMessage);
    }

    private static bool TryEvaluateTrianglePatch(
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationSmoothPatchData patchData,
        int triangleIndex,
        Vector2 point,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        if (!TryCalculateMacroBarycentric(
            topology,
            triangleIndex,
            point,
            out double alpha,
            out double beta,
            out double gamma,
            out _,
            out _,
            out _,
            out errorMessage))
        {
            return false;
        }

        if (!TryNormalizeMacroBarycentric(
            ref alpha,
            ref beta,
            ref gamma,
            out errorMessage))
        {
            return false;
        }

        int subpatchIndex;

        if (gamma <= alpha && gamma <= beta)
        {
            subpatchIndex = 0;
        }
        else if (alpha <= beta && alpha <= gamma)
        {
            subpatchIndex = 1;
        }
        else
        {
            subpatchIndex = 2;
        }

        return TryEvaluateTrianglePatchSubpatch(
            topology,
            patchData,
            triangleIndex,
            subpatchIndex,
            point,
            true,
            out height,
            out gradientXZ,
            out errorMessage);
    }

    private static bool TryEvaluateTrianglePatchSubpatch(
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationSmoothPatchData patchData,
        int triangleIndex,
        int subpatchIndex,
        Vector2 point,
        bool normalizeMacroBarycentric,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        if (!patchData.TryGetTrianglePatch(
            triangleIndex,
            out TerrainNodeElevationSmoothTrianglePatch trianglePatch))
        {
            errorMessage =
                "Smooth triangle patch index is invalid.";
            return false;
        }

        if (!TryCalculateMacroBarycentric(
            topology,
            triangleIndex,
            point,
            out double alpha,
            out double beta,
            out double gamma,
            out Double2 gradientAlpha,
            out Double2 gradientBeta,
            out Double2 gradientGamma,
            out errorMessage))
        {
            return false;
        }

        if (normalizeMacroBarycentric &&
            !TryNormalizeMacroBarycentric(
                ref alpha,
                ref beta,
                ref gamma,
                out errorMessage))
        {
            return false;
        }

        if (!TryGetSubpatchCoordinates(
            subpatchIndex,
            alpha,
            beta,
            gamma,
            gradientAlpha,
            gradientBeta,
            gradientGamma,
            out double u,
            out double v,
            out double w,
            out Double2 gradientU,
            out Double2 gradientV,
            out Double2 gradientW,
            out errorMessage))
        {
            return false;
        }

        if (!TryNormalizeSubpatchBarycentric(
            ref u,
            ref v,
            ref w,
            out errorMessage))
        {
            return false;
        }

        TerrainNodeElevationSmoothCubicPatch cubicPatch =
            trianglePatch.GetSubpatch(subpatchIndex);

        return TryEvaluateBernstein(
            cubicPatch,
            u,
            v,
            w,
            gradientU,
            gradientV,
            gradientW,
            out height,
            out gradientXZ,
            out errorMessage);
    }

    private static bool TryCalculateMacroBarycentric(
        TerrainNodeElevationTopology topology,
        int triangleIndex,
        Vector2 point,
        out double alpha,
        out double beta,
        out double gamma,
        out Double2 gradientAlpha,
        out Double2 gradientBeta,
        out Double2 gradientGamma,
        out string errorMessage)
    {
        alpha = 0.0;
        beta = 0.0;
        gamma = 0.0;
        gradientAlpha = default(Double2);
        gradientBeta = default(Double2);
        gradientGamma = default(Double2);
        errorMessage = "";

        if (
            triangleIndex < 0 ||
            triangleIndex >= topology.TriangleCount)
        {
            errorMessage =
                "Smooth macro-triangle index is outside the topology range.";
            return false;
        }

        TerrainNodeElevationTopologyTriangle triangle =
            topology.Triangles[triangleIndex];

        Vector2 a =
            topology.Vertices[triangle.VertexA].PositionXZ;

        Vector2 b =
            topology.Vertices[triangle.VertexB].PositionXZ;

        Vector2 c =
            topology.Vertices[triangle.VertexC].PositionXZ;

        double determinant =
            ((double)b.y - c.y) * ((double)a.x - c.x) +
            ((double)c.x - b.x) * ((double)a.y - c.y);

        if (
            !IsFinite(determinant) ||
            Math.Abs(determinant) <= 0.0)
        {
            errorMessage =
                "Smooth macro-triangle barycentric determinant is invalid.";
            return false;
        }

        gradientAlpha =
            new Double2(
                ((double)b.y - c.y) / determinant,
                ((double)c.x - b.x) / determinant);

        gradientBeta =
            new Double2(
                ((double)c.y - a.y) / determinant,
                ((double)a.x - c.x) / determinant);

        gradientGamma =
            new Double2(
                -gradientAlpha.X - gradientBeta.X,
                -gradientAlpha.Z - gradientBeta.Z);

        double dx =
            (double)point.x - c.x;

        double dz =
            (double)point.y - c.y;

        alpha =
            gradientAlpha.X * dx +
            gradientAlpha.Z * dz;

        beta =
            gradientBeta.X * dx +
            gradientBeta.Z * dz;

        gamma =
            1.0 - alpha - beta;

        if (
            !IsFinite(alpha) ||
            !IsFinite(beta) ||
            !IsFinite(gamma) ||
            !IsFinite(gradientAlpha) ||
            !IsFinite(gradientBeta) ||
            !IsFinite(gradientGamma))
        {
            errorMessage =
                "Smooth macro barycentric evaluation produced a non-finite value.";
            return false;
        }

        return true;
    }

    private static bool TryNormalizeMacroBarycentric(
        ref double alpha,
        ref double beta,
        ref double gamma,
        out string errorMessage)
    {
        errorMessage = "";

        if (!TrySnapBarycentric(ref alpha) ||
            !TrySnapBarycentric(ref beta) ||
            !TrySnapBarycentric(ref gamma))
        {
            errorMessage =
                "Smooth sample is materially outside the selected macro-triangle.";
            return false;
        }

        double sum =
            alpha + beta + gamma;

        if (
            !IsFinite(sum) ||
            Math.Abs(sum - 1.0) > BarycentricTolerance * 8.0)
        {
            errorMessage =
                "Smooth macro barycentric coordinates do not sum to one.";
            return false;
        }

        if (sum != 1.0)
        {
            alpha /= sum;
            beta /= sum;
            gamma /= sum;
        }

        return true;
    }

    private static bool TryGetSubpatchCoordinates(
        int subpatchIndex,
        double alpha,
        double beta,
        double gamma,
        Double2 gradientAlpha,
        Double2 gradientBeta,
        Double2 gradientGamma,
        out double u,
        out double v,
        out double w,
        out Double2 gradientU,
        out Double2 gradientV,
        out Double2 gradientW,
        out string errorMessage)
    {
        u = 0.0;
        v = 0.0;
        w = 0.0;
        gradientU = default(Double2);
        gradientV = default(Double2);
        gradientW = default(Double2);
        errorMessage = "";

        switch (subpatchIndex)
        {
            case 0:
                u = alpha - gamma;
                v = beta - gamma;
                w = 3.0 * gamma;
                gradientU = gradientAlpha - gradientGamma;
                gradientV = gradientBeta - gradientGamma;
                gradientW = gradientGamma * 3.0;
                break;

            case 1:
                u = beta - alpha;
                v = gamma - alpha;
                w = 3.0 * alpha;
                gradientU = gradientBeta - gradientAlpha;
                gradientV = gradientGamma - gradientAlpha;
                gradientW = gradientAlpha * 3.0;
                break;

            case 2:
                u = gamma - beta;
                v = alpha - beta;
                w = 3.0 * beta;
                gradientU = gradientGamma - gradientBeta;
                gradientV = gradientAlpha - gradientBeta;
                gradientW = gradientBeta * 3.0;
                break;

            default:
                errorMessage =
                    "Smooth subpatch index is invalid.";
                return false;
        }

        if (
            !IsFinite(u) ||
            !IsFinite(v) ||
            !IsFinite(w) ||
            !IsFinite(gradientU) ||
            !IsFinite(gradientV) ||
            !IsFinite(gradientW))
        {
            errorMessage =
                "Smooth subpatch barycentric transform produced non-finite data.";
            return false;
        }

        return true;
    }

    private static bool TryNormalizeSubpatchBarycentric(
        ref double u,
        ref double v,
        ref double w,
        out string errorMessage)
    {
        errorMessage = "";

        if (!TrySnapBarycentric(ref u) ||
            !TrySnapBarycentric(ref v) ||
            !TrySnapBarycentric(ref w))
        {
            errorMessage =
                "Smooth sample is materially outside the selected HCT subpatch.";
            return false;
        }

        double sum =
            u + v + w;

        if (
            !IsFinite(sum) ||
            Math.Abs(sum - 1.0) > BarycentricTolerance * 16.0)
        {
            errorMessage =
                "Smooth subpatch barycentric coordinates do not sum to one.";
            return false;
        }

        if (sum != 1.0)
        {
            u /= sum;
            v /= sum;
            w /= sum;
        }

        return true;
    }

    private static bool TrySnapBarycentric(ref double value)
    {
        if (!IsFinite(value))
        {
            return false;
        }

        if (value < 0.0)
        {
            if (value < -BarycentricTolerance)
            {
                return false;
            }

            value = 0.0;
        }
        else if (value > 1.0)
        {
            if (value > 1.0 + BarycentricTolerance)
            {
                return false;
            }

            value = 1.0;
        }

        return true;
    }

    private static bool TryEvaluateBernstein(
        TerrainNodeElevationSmoothCubicPatch patch,
        double u,
        double v,
        double w,
        Double2 gradientU,
        Double2 gradientV,
        Double2 gradientW,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        double u2 = u * u;
        double v2 = v * v;
        double w2 = w * w;

        double[] basis =
        {
            u2 * u,
            v2 * v,
            w2 * w,
            3.0 * u2 * v,
            3.0 * u2 * w,
            3.0 * u * v2,
            3.0 * v2 * w,
            3.0 * u * w2,
            3.0 * v * w2,
            6.0 * u * v * w
        };

        double[] du =
        {
            3.0 * u2,
            0.0,
            0.0,
            6.0 * u * v,
            6.0 * u * w,
            3.0 * v2,
            0.0,
            3.0 * w2,
            0.0,
            6.0 * v * w
        };

        double[] dv =
        {
            0.0,
            3.0 * v2,
            0.0,
            3.0 * u2,
            0.0,
            6.0 * u * v,
            6.0 * v * w,
            0.0,
            3.0 * w2,
            6.0 * u * w
        };

        double[] dw =
        {
            0.0,
            0.0,
            3.0 * w2,
            0.0,
            3.0 * u2,
            0.0,
            3.0 * v2,
            6.0 * u * w,
            6.0 * v * w,
            6.0 * u * v
        };

        double evaluatedHeight = 0.0;
        Double2 evaluatedGradient =
            new Double2(0.0, 0.0);

        for (int index = 0; index < 10; index++)
        {
            double coefficient =
                patch.GetCoefficient(index);

            if (!IsFinite(coefficient))
            {
                errorMessage =
                    "Smooth patch contains a non-finite Bernstein coefficient.";
                return false;
            }

            evaluatedHeight +=
                coefficient * basis[index];

            Double2 basisGradient =
                gradientU * du[index] +
                gradientV * dv[index] +
                gradientW * dw[index];

            evaluatedGradient =
                evaluatedGradient +
                basisGradient * coefficient;
        }

        return TryConvertHeightAndGradient(
            evaluatedHeight,
            evaluatedGradient,
            out height,
            out gradientXZ,
            out errorMessage);
    }

    private static bool TryEvaluateHullExterior(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        Vector2 point,
        out float height,
        out Vector2 gradientXZ,
        out int selectedHullEdgeIndex,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        selectedHullEdgeIndex = -1;
        errorMessage = "";

        if (
            topology == null ||
            topology.Kind != TerrainNodeElevationTopologyKind.Triangulated ||
            topology.HullEdgeCount <= 0)
        {
            errorMessage =
                "Triangulated Smooth topology does not contain a usable convex hull.";
            return false;
        }

        double bestDistanceSquared =
            double.PositiveInfinity;

        double bestT = 0.0;
        double bestRawT = 0.0;

        TerrainNodeElevationTopologyEdge bestEdge =
            default(TerrainNodeElevationTopologyEdge);

        for (int edgeIndex = 0;
            edgeIndex < topology.HullEdgeCount;
            edgeIndex++)
        {
            TerrainNodeElevationTopologyEdge edge =
                topology.HullEdges[edgeIndex];

            Vector2 a =
                topology.Vertices[edge.VertexA].PositionXZ;

            Vector2 b =
                topology.Vertices[edge.VertexB].PositionXZ;

            if (!TryProjectPointOntoSegment(
                point,
                a,
                b,
                out double rawT,
                out double clampedT,
                out double distanceSquared,
                out errorMessage))
            {
                return false;
            }

            bool better =
                selectedHullEdgeIndex < 0 ||
                distanceSquared <
                    bestDistanceSquared - HullDistanceTieToleranceSquared;

            bool tied =
                selectedHullEdgeIndex >= 0 &&
                Math.Abs(distanceSquared - bestDistanceSquared) <=
                    HullDistanceTieToleranceSquared;

            if (better || (tied && edgeIndex < selectedHullEdgeIndex))
            {
                selectedHullEdgeIndex = edgeIndex;
                bestDistanceSquared = distanceSquared;
                bestRawT = rawT;
                bestT = clampedT;
                bestEdge = edge;
            }
        }

        if (selectedHullEdgeIndex < 0)
        {
            errorMessage =
                "No valid convex-hull edge was available for Smooth exterior evaluation.";
            return false;
        }

        if (!TryGetHermiteEndpointData(
            source,
            topology,
            gradients,
            bestEdge.VertexA,
            out Vector2 positionA,
            out double elevationA,
            out Double2 gradientA,
            out errorMessage) ||
            !TryGetHermiteEndpointData(
                source,
                topology,
                gradients,
                bestEdge.VertexB,
                out Vector2 positionB,
                out double elevationB,
                out Double2 gradientB,
                out errorMessage))
        {
            return false;
        }

        Double2 delta =
            new Double2(
                (double)positionB.x - positionA.x,
                (double)positionB.y - positionA.y);

        double lengthSquared =
            Dot(delta, delta);

        if (!IsUsableSegmentLength(lengthSquared))
        {
            errorMessage =
                "Selected Smooth hull edge has an unsafe length.";
            return false;
        }

        double mA =
            Dot(gradientA, delta);

        double mB =
            Dot(gradientB, delta);

        if (!TryEvaluateHermite(
            elevationA,
            elevationB,
            mA,
            mB,
            bestT,
            out double evaluatedHeight,
            out double derivativeT,
            out errorMessage))
        {
            return false;
        }

        Double2 exteriorGradient =
            (bestRawT <= 0.0 || bestRawT >= 1.0)
                ? new Double2(0.0, 0.0)
                : delta * (derivativeT / lengthSquared);

        return TryConvertHeightAndGradient(
            evaluatedHeight,
            exteriorGradient,
            out height,
            out gradientXZ,
            out errorMessage);
    }

    private static bool TryGetHermiteEndpointData(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        int vertexIndex,
        out Vector2 position,
        out double elevation,
        out Double2 gradient,
        out string errorMessage)
    {
        position = Vector2.zero;
        elevation = 0.0;
        gradient = default(Double2);
        errorMessage = "";

        if (
            vertexIndex < 0 ||
            vertexIndex >= topology.VertexCount)
        {
            errorMessage =
                "Smooth Hermite endpoint vertex index is invalid.";
            return false;
        }

        TerrainNodeElevationTopologyVertex vertex =
            topology.Vertices[vertexIndex];

        if (
            vertex.SourceNodeIndex < 0 ||
            vertex.SourceNodeIndex >= source.NodeCount ||
            source.Nodes[vertex.SourceNodeIndex] == null ||
            !gradients.TryGetGradient(
                vertexIndex,
                out TerrainNodeElevationGradient nodeGradient))
        {
            errorMessage =
                "Smooth Hermite endpoint could not resolve source/gradient data.";
            return false;
        }

        position =
            vertex.PositionXZ;

        elevation =
            source.Nodes[vertex.SourceNodeIndex].Elevation;

        gradient =
            new Double2(
                nodeGradient.GradientX,
                nodeGradient.GradientZ);

        return
            IsFinite(position) &&
            IsFinite(elevation) &&
            IsFinite(gradient);
    }

    private static bool TryEvaluateHermite(
        double hA,
        double hB,
        double mA,
        double mB,
        double t,
        out double height,
        out double derivativeT,
        out string errorMessage)
    {
        height = 0.0;
        derivativeT = 0.0;
        errorMessage = "";

        if (
            !IsFinite(hA) ||
            !IsFinite(hB) ||
            !IsFinite(mA) ||
            !IsFinite(mB) ||
            !IsFinite(t))
        {
            errorMessage =
                "Smooth Hermite input contains a non-finite value.";
            return false;
        }

        double t2 = t * t;
        double t3 = t2 * t;

        double h00 =
            2.0 * t3 - 3.0 * t2 + 1.0;

        double h10 =
            t3 - 2.0 * t2 + t;

        double h01 =
            -2.0 * t3 + 3.0 * t2;

        double h11 =
            t3 - t2;

        height =
            h00 * hA +
            h10 * mA +
            h01 * hB +
            h11 * mB;

        double dh00 =
            6.0 * t2 - 6.0 * t;

        double dh10 =
            3.0 * t2 - 4.0 * t + 1.0;

        double dh01 =
            -6.0 * t2 + 6.0 * t;

        double dh11 =
            3.0 * t2 - 2.0 * t;

        derivativeT =
            dh00 * hA +
            dh10 * mA +
            dh01 * hB +
            dh11 * mB;

        if (
            !IsFinite(height) ||
            !IsFinite(derivativeT))
        {
            errorMessage =
                "Smooth Hermite evaluation produced a non-finite result.";
            return false;
        }

        return true;
    }

    private static bool TryProjectPointOntoSegment(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        out double rawT,
        out double clampedT,
        out double distanceSquared,
        out string errorMessage)
    {
        rawT = 0.0;
        clampedT = 0.0;
        distanceSquared = 0.0;
        errorMessage = "";

        double dx =
            (double)b.x - a.x;

        double dz =
            (double)b.y - a.y;

        double lengthSquared =
            dx * dx + dz * dz;

        if (!IsUsableSegmentLength(lengthSquared))
        {
            errorMessage =
                "Smooth segment projection encountered an unsafe edge length.";
            return false;
        }

        rawT =
            (((double)point.x - a.x) * dx +
             ((double)point.y - a.y) * dz) /
            lengthSquared;

        if (!IsFinite(rawT))
        {
            errorMessage =
                "Smooth segment projection produced a non-finite parameter.";
            return false;
        }

        clampedT =
            Clamp01(rawT);

        double projectedX =
            a.x + clampedT * dx;

        double projectedZ =
            a.y + clampedT * dz;

        double distanceX =
            (double)point.x - projectedX;

        double distanceZ =
            (double)point.y - projectedZ;

        distanceSquared =
            distanceX * distanceX +
            distanceZ * distanceZ;

        if (!IsFinite(distanceSquared))
        {
            errorMessage =
                "Smooth segment projection produced a non-finite distance.";
            return false;
        }

        return true;
    }

    private static bool TryGetVertexHeight(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        int vertexIndex,
        out float height,
        out string errorMessage)
    {
        height = 0f;
        errorMessage = "";

        if (
            vertexIndex < 0 ||
            vertexIndex >= topology.VertexCount)
        {
            errorMessage =
                "Smooth topology vertex index is invalid.";
            return false;
        }

        int sourceNodeIndex =
            topology.Vertices[vertexIndex].SourceNodeIndex;

        if (
            sourceNodeIndex < 0 ||
            sourceNodeIndex >= source.NodeCount ||
            source.Nodes[sourceNodeIndex] == null)
        {
            errorMessage =
                "Smooth topology vertex has an invalid source mapping.";
            return false;
        }

        height =
            source.Nodes[sourceNodeIndex].Elevation;

        return IsFinite(height);
    }

    private static bool TryConvertHeightAndGradient(
        double heightValue,
        Double2 gradientValue,
        out float height,
        out Vector2 gradientXZ,
        out string errorMessage)
    {
        height = 0f;
        gradientXZ = Vector2.zero;
        errorMessage = "";

        if (
            !IsFinite(heightValue) ||
            !IsFinite(gradientValue) ||
            heightValue > float.MaxValue ||
            heightValue < -float.MaxValue ||
            gradientValue.X > float.MaxValue ||
            gradientValue.X < -float.MaxValue ||
            gradientValue.Z > float.MaxValue ||
            gradientValue.Z < -float.MaxValue)
        {
            errorMessage =
                "Smooth evaluation produced a non-finite or unsupported result.";
            return false;
        }

        height =
            (float)heightValue;

        gradientXZ =
            new Vector2(
                (float)gradientValue.X,
                (float)gradientValue.Z);

        if (
            !IsFinite(height) ||
            !IsFinite(gradientXZ))
        {
            errorMessage =
                "Smooth evaluation could not safely convert its result to float.";
            return false;
        }

        return true;
    }

    private static bool IsUsableSegmentLength(double lengthSquared)
    {
        double minimum =
            TerrainNodeElevationGeometryUtility
                .MinimumTriangulationVertexSeparation;

        double minimumSquared =
            minimum * minimum;

        return
            IsFinite(lengthSquared) &&
            lengthSquared >= minimumSquared;
    }

    private static double Clamp01(double value)
    {
        if (value <= 0.0)
        {
            return 0.0;
        }

        if (value >= 1.0)
        {
            return 1.0;
        }

        return value;
    }

    private static double Dot(Double2 a, Double2 b)
    {
        return
            a.X * b.X +
            a.Z * b.Z;
    }

    private static bool IsFinite(Vector2 value)
    {
        return
            IsFinite(value.x) &&
            IsFinite(value.y);
    }

    private static bool IsFinite(Double2 value)
    {
        return
            IsFinite(value.X) &&
            IsFinite(value.Z);
    }

    private static bool IsFinite(float value)
    {
        return
            !float.IsNaN(value) &&
            !float.IsInfinity(value);
    }

    private static bool IsFinite(double value)
    {
        return
            !double.IsNaN(value) &&
            !double.IsInfinity(value);
    }
}
