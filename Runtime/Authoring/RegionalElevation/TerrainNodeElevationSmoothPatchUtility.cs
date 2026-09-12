using System;
using System.Collections.Generic;
using UnityEngine;

/*
 * Package I6 reduced Hsieh-Clough-Tocher patch construction.
 *
 * I2 remains the only triangulation authority. For each I2 triangle ABC this
 * utility creates the derived centroid G and solves one coupled reduced-HCT
 * macro-element containing exactly three cubic Bernstein-Bezier subpatches:
 * ABG, BCG, CAG.
 *
 * The twelve reduced-HCT degrees of freedom are the three source heights, the
 * six I5 gradient components, and one derived midpoint normal derivative on
 * each canonical outer edge. Internal spoke constraints couple all three
 * cubics into one C1 macro-element. Coefficients are solved in double precision
 * and stored as float for deterministic future GPU upload/evaluation.
 */
public static class TerrainNodeElevationSmoothPatchUtility
{
    private const int CoefficientsPerSubpatch = 10;
    private const int SubpatchCount = 3;
    private const int UnknownCount =
        CoefficientsPerSubpatch * SubpatchCount;

    private const int ConstraintCount = 33;

    private const double SolverRankTolerance = 1e-11;
    private const double SolverResidualTolerance = 2e-7;

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

    private struct SubtriangleGeometry
    {
        public Double2 A;
        public Double2 B;
        public Double2 C;

        public Double2 GradientU;
        public Double2 GradientV;
        public Double2 GradientW;

        public SubtriangleGeometry(
            Double2 a,
            Double2 b,
            Double2 c,
            Double2 gradientU,
            Double2 gradientV,
            Double2 gradientW)
        {
            A = a;
            B = b;
            C = c;
            GradientU = gradientU;
            GradientV = gradientV;
            GradientW = gradientW;
        }
    }

    public static bool TryBuild(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        out TerrainNodeElevationSmoothPatchData patchData,
        out string errorMessage)
    {
        patchData = null;
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
                "Smooth patch construction requires topology vertex count " +
                "to match the source node count.";
            return false;
        }

        if (gradients.GradientCount != topology.VertexCount)
        {
            errorMessage =
                "Smooth patch construction requires one I5 gradient for " +
                "every topology vertex.";
            return false;
        }

        if (!TryValidateSourceMapping(
            source,
            topology,
            gradients,
            out errorMessage))
        {
            return false;
        }

        if (topology.Kind != TerrainNodeElevationTopologyKind.Triangulated)
        {
            if (topology.TriangleCount != 0)
            {
                errorMessage =
                    "A non-triangulated regional topology cannot contain " +
                    "Smooth HCT macro-triangles.";
                return false;
            }

            patchData =
                new TerrainNodeElevationSmoothPatchData(
                    new TerrainNodeElevationSmoothTrianglePatch[0]);
            return true;
        }

        if (topology.TriangleCount <= 0)
        {
            errorMessage =
                "Triangulated regional topology does not contain triangles.";
            return false;
        }

        TerrainNodeElevationSmoothTrianglePatch[] patches =
            new TerrainNodeElevationSmoothTrianglePatch[topology.TriangleCount];

        for (int triangleIndex = 0;
            triangleIndex < topology.TriangleCount;
            triangleIndex++)
        {
            if (!TryBuildTrianglePatch(
                source,
                topology,
                gradients,
                triangleIndex,
                out patches[triangleIndex],
                out errorMessage))
            {
                errorMessage =
                    $"Could not build Smooth macro-triangle {triangleIndex}. " +
                    errorMessage;
                return false;
            }
        }

        patchData =
            new TerrainNodeElevationSmoothPatchData(
                patches);

        return true;
    }

    private static bool TryValidateSourceMapping(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        out string errorMessage)
    {
        errorMessage = "";

        bool[] mappedSourceNodes =
            new bool[source.NodeCount];

        for (int vertexIndex = 0;
            vertexIndex < topology.VertexCount;
            vertexIndex++)
        {
            TerrainNodeElevationTopologyVertex vertex =
                topology.Vertices[vertexIndex];

            int sourceNodeIndex =
                vertex.SourceNodeIndex;

            if (
                sourceNodeIndex < 0 ||
                sourceNodeIndex >= source.NodeCount)
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} contains invalid source " +
                    $"node index {sourceNodeIndex}.";
                return false;
            }

            if (mappedSourceNodes[sourceNodeIndex])
            {
                errorMessage =
                    $"Topology source mapping duplicates source node index " +
                    $"{sourceNodeIndex}.";
                return false;
            }

            mappedSourceNodes[sourceNodeIndex] = true;

            TerrainElevationNode node =
                source.Nodes[sourceNodeIndex];

            if (node == null)
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} maps to a null source node.";
                return false;
            }

            if (!node.TryGetStoredPositionXZInternal(
                out Vector2 storedPosition,
                out string positionError))
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} could not resolve valid " +
                    "source geometry. " +
                    positionError;
                return false;
            }

            if (storedPosition != vertex.PositionXZ)
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} is stale relative to " +
                    "its mapped source node position.";
                return false;
            }

            if (!gradients.TryGetGradient(
                vertexIndex,
                out TerrainNodeElevationGradient gradient) ||
                !IsFinite(gradient.GradientX) ||
                !IsFinite(gradient.GradientZ))
            {
                errorMessage =
                    $"Topology vertex {vertexIndex} does not have a finite " +
                    "I5 gradient.";
                return false;
            }
        }

        for (int sourceIndex = 0;
            sourceIndex < mappedSourceNodes.Length;
            sourceIndex++)
        {
            if (!mappedSourceNodes[sourceIndex])
            {
                errorMessage =
                    $"Topology does not map source node index {sourceIndex}.";
                return false;
            }
        }

        return true;
    }

    private static bool TryBuildTrianglePatch(
        TerrainNodeElevationSource source,
        TerrainNodeElevationTopology topology,
        TerrainNodeElevationGradientData gradients,
        int triangleIndex,
        out TerrainNodeElevationSmoothTrianglePatch patch,
        out string errorMessage)
    {
        patch =
            default(TerrainNodeElevationSmoothTrianglePatch);
        errorMessage = "";

        if (
            triangleIndex < 0 ||
            triangleIndex >= topology.TriangleCount)
        {
            errorMessage = "Triangle index is outside the topology range.";
            return false;
        }

        TerrainNodeElevationTopologyTriangle triangle =
            topology.Triangles[triangleIndex];

        int[] vertexIndices =
        {
            triangle.VertexA,
            triangle.VertexB,
            triangle.VertexC
        };

        Double2[] positions =
            new Double2[3];

        double[] elevations =
            new double[3];

        Double2[] vertexGradients =
            new Double2[3];

        for (int localIndex = 0; localIndex < 3; localIndex++)
        {
            int vertexIndex =
                vertexIndices[localIndex];

            if (
                vertexIndex < 0 ||
                vertexIndex >= topology.VertexCount)
            {
                errorMessage =
                    $"Triangle contains invalid topology vertex index " +
                    $"{vertexIndex}.";
                return false;
            }

            TerrainNodeElevationTopologyVertex vertex =
                topology.Vertices[vertexIndex];

            int sourceNodeIndex =
                vertex.SourceNodeIndex;

            TerrainElevationNode node =
                source.Nodes[sourceNodeIndex];

            if (!gradients.TryGetGradient(
                vertexIndex,
                out TerrainNodeElevationGradient gradient))
            {
                errorMessage =
                    $"Triangle vertex {vertexIndex} has no I5 gradient.";
                return false;
            }

            positions[localIndex] =
                ToDouble2(vertex.PositionXZ);

            elevations[localIndex] =
                node.Elevation;

            vertexGradients[localIndex] =
                new Double2(
                    gradient.GradientX,
                    gradient.GradientZ);

            if (
                !IsFinite(positions[localIndex]) ||
                !IsFinite(elevations[localIndex]) ||
                !IsFinite(vertexGradients[localIndex]))
            {
                errorMessage =
                    "Triangle contains non-finite Smooth input data.";
                return false;
            }
        }

        Double2 centroid =
            new Double2(
                (positions[0].X + positions[1].X + positions[2].X) / 3.0,
                (positions[0].Z + positions[1].Z + positions[2].Z) / 3.0);

        SubtriangleGeometry[] subtriangles =
            new SubtriangleGeometry[3];

        if (!TryCreateSubtriangle(
            positions[0],
            positions[1],
            centroid,
            out subtriangles[0],
            out errorMessage) ||
            !TryCreateSubtriangle(
                positions[1],
                positions[2],
                centroid,
                out subtriangles[1],
                out errorMessage) ||
            !TryCreateSubtriangle(
                positions[2],
                positions[0],
                centroid,
                out subtriangles[2],
                out errorMessage))
        {
            return false;
        }

        double[,] matrix =
            new double[ConstraintCount, UnknownCount];

        double[] rhs =
            new double[ConstraintCount];

        int row = 0;

        /*
         * Exact value and full world gradient at both original vertices that
         * touch each subpatch. Duplicating these constraints at each incident
         * subpatch is deliberate and participates in the coupled solve.
         */
        int[,] vertexOccurrences =
        {
            { 0, 0 },
            { 0, 1 },
            { 1, 1 },
            { 1, 2 },
            { 2, 2 },
            { 2, 0 }
        };

        for (int occurrence = 0;
            occurrence < vertexOccurrences.GetLength(0);
            occurrence++)
        {
            int subpatchIndex =
                vertexOccurrences[occurrence, 0];

            int vertexLocalIndex =
                vertexOccurrences[occurrence, 1];

            if (!TryAddValueConstraint(
                matrix,
                rhs,
                ref row,
                subtriangles,
                subpatchIndex,
                positions[vertexLocalIndex],
                elevations[vertexLocalIndex],
                out errorMessage) ||
                !TryAddDirectionalConstraint(
                    matrix,
                    rhs,
                    ref row,
                    subtriangles,
                    subpatchIndex,
                    positions[vertexLocalIndex],
                    new Double2(1.0, 0.0),
                    vertexGradients[vertexLocalIndex].X,
                    out errorMessage) ||
                !TryAddDirectionalConstraint(
                    matrix,
                    rhs,
                    ref row,
                    subtriangles,
                    subpatchIndex,
                    positions[vertexLocalIndex],
                    new Double2(0.0, 1.0),
                    vertexGradients[vertexLocalIndex].Z,
                    out errorMessage))
            {
                return false;
            }
        }

        /*
         * One reduced-HCT edge-normal degree of freedom per canonical I2 edge.
         * The normal is derived from canonical min-index -> max-index ordering,
         * which matches TerrainNodeElevationTopologyEdge canonicalization.
         */
        int[,] outerEdges =
        {
            { 0, 1, 0 },
            { 1, 2, 1 },
            { 2, 0, 2 }
        };

        for (int edgeLocalIndex = 0;
            edgeLocalIndex < outerEdges.GetLength(0);
            edgeLocalIndex++)
        {
            int localA =
                outerEdges[edgeLocalIndex, 0];

            int localB =
                outerEdges[edgeLocalIndex, 1];

            int subpatchIndex =
                outerEdges[edgeLocalIndex, 2];

            if (!TryGetCanonicalEdgeNormal(
                vertexIndices[localA],
                vertexIndices[localB],
                positions[localA],
                positions[localB],
                out Double2 normal,
                out errorMessage))
            {
                return false;
            }

            Double2 midpoint =
                new Double2(
                    0.5 * (positions[localA].X + positions[localB].X),
                    0.5 * (positions[localA].Z + positions[localB].Z));

            double normalDerivative =
                0.5 *
                (
                    Dot(vertexGradients[localA], normal) +
                    Dot(vertexGradients[localB], normal)
                );

            if (!TryAddDirectionalConstraint(
                matrix,
                rhs,
                ref row,
                subtriangles,
                subpatchIndex,
                midpoint,
                normal,
                normalDerivative,
                out errorMessage))
            {
                return false;
            }
        }

        /*
         * Internal centroid spokes. At each original vertex both incident
         * subpatches already share value and full gradient. For each spoke,
         * matching height at two additional interior points makes the cubic
         * edge restrictions identical. Matching the spoke-normal derivative at
         * the same two points, together with the already-shared endpoint
         * gradient, makes the quadratic normal-derivative restrictions
         * identical. This enforces C1 continuity along the complete spoke.
         */
        int[,] internalSpokes =
        {
            { 0, 0, 2 },
            { 1, 0, 1 },
            { 2, 1, 2 }
        };

        double[] spokeParameters =
        {
            1.0 / 3.0,
            2.0 / 3.0
        };

        for (int spokeIndex = 0;
            spokeIndex < internalSpokes.GetLength(0);
            spokeIndex++)
        {
            int vertexLocalIndex =
                internalSpokes[spokeIndex, 0];

            int firstSubpatch =
                internalSpokes[spokeIndex, 1];

            int secondSubpatch =
                internalSpokes[spokeIndex, 2];

            Double2 spokeVector =
                centroid - positions[vertexLocalIndex];

            if (!TryNormalizePerpendicular(
                spokeVector,
                out Double2 spokeNormal,
                out errorMessage))
            {
                return false;
            }

            for (int parameterIndex = 0;
                parameterIndex < spokeParameters.Length;
                parameterIndex++)
            {
                double t =
                    spokeParameters[parameterIndex];

                Double2 sample =
                    positions[vertexLocalIndex] +
                    spokeVector * t;

                if (!TryAddDifferenceConstraint(
                    matrix,
                    rhs,
                    ref row,
                    subtriangles,
                    firstSubpatch,
                    secondSubpatch,
                    sample,
                    false,
                    default(Double2),
                    out errorMessage) ||
                    !TryAddDifferenceConstraint(
                        matrix,
                        rhs,
                        ref row,
                        subtriangles,
                        firstSubpatch,
                        secondSubpatch,
                        sample,
                        true,
                        spokeNormal,
                        out errorMessage))
                {
                    return false;
                }
            }
        }

        if (row != ConstraintCount)
        {
            errorMessage =
                $"Smooth HCT constraint assembly produced {row} rows; " +
                $"expected {ConstraintCount}.";
            return false;
        }

        if (!TrySolveLeastSquares(
            matrix,
            rhs,
            out double[] solution,
            out errorMessage))
        {
            return false;
        }

        TerrainNodeElevationSmoothCubicPatch[] cubicPatches =
            new TerrainNodeElevationSmoothCubicPatch[3];

        for (int subpatchIndex = 0;
            subpatchIndex < 3;
            subpatchIndex++)
        {
            int offset =
                subpatchIndex * CoefficientsPerSubpatch;

            float[] values =
                new float[CoefficientsPerSubpatch];

            for (int coefficientIndex = 0;
                coefficientIndex < CoefficientsPerSubpatch;
                coefficientIndex++)
            {
                double coefficient =
                    solution[offset + coefficientIndex];

                if (!TryConvertToFiniteFloat(
                    coefficient,
                    out values[coefficientIndex]))
                {
                    errorMessage =
                        $"Smooth HCT coefficient {coefficientIndex} in " +
                        $"subpatch {subpatchIndex} is outside the supported " +
                        "finite float range.";
                    return false;
                }
            }

            cubicPatches[subpatchIndex] =
                new TerrainNodeElevationSmoothCubicPatch(
                    values[0],
                    values[1],
                    values[2],
                    values[3],
                    values[4],
                    values[5],
                    values[6],
                    values[7],
                    values[8],
                    values[9]);
        }

        patch =
            new TerrainNodeElevationSmoothTrianglePatch(
                cubicPatches[0],
                cubicPatches[1],
                cubicPatches[2]);

        return true;
    }

    private static bool TryCreateSubtriangle(
        Double2 a,
        Double2 b,
        Double2 c,
        out SubtriangleGeometry geometry,
        out string errorMessage)
    {
        geometry =
            default(SubtriangleGeometry);
        errorMessage = "";

        double determinant =
            (b.Z - c.Z) * (a.X - c.X) +
            (c.X - b.X) * (a.Z - c.Z);

        if (!IsFinite(determinant))
        {
            errorMessage =
                "Smooth subtriangle barycentric determinant is non-finite.";
            return false;
        }

        double minimumSeparation =
            TerrainNodeElevationGeometryUtility
                .MinimumTriangulationVertexSeparation;

        double minimumAreaScale =
            minimumSeparation * minimumSeparation;

        if (Math.Abs(determinant) <= minimumAreaScale * 1e-6)
        {
            errorMessage =
                "Smooth subtriangle is numerically degenerate.";
            return false;
        }

        Double2 gradientU =
            new Double2(
                (b.Z - c.Z) / determinant,
                (c.X - b.X) / determinant);

        Double2 gradientV =
            new Double2(
                (c.Z - a.Z) / determinant,
                (a.X - c.X) / determinant);

        Double2 gradientW =
            new Double2(
                -gradientU.X - gradientV.X,
                -gradientU.Z - gradientV.Z);

        if (
            !IsFinite(gradientU) ||
            !IsFinite(gradientV) ||
            !IsFinite(gradientW))
        {
            errorMessage =
                "Smooth subtriangle barycentric gradients are non-finite.";
            return false;
        }

        geometry =
            new SubtriangleGeometry(
                a,
                b,
                c,
                gradientU,
                gradientV,
                gradientW);

        return true;
    }

    private static bool TryAddValueConstraint(
        double[,] matrix,
        double[] rhs,
        ref int row,
        SubtriangleGeometry[] subtriangles,
        int subpatchIndex,
        Double2 point,
        double target,
        out string errorMessage)
    {
        errorMessage = "";

        if (!TryEvaluateBasis(
            subtriangles[subpatchIndex],
            point,
            out double[] basis,
            out _,
            out errorMessage))
        {
            return false;
        }

        int offset =
            subpatchIndex * CoefficientsPerSubpatch;

        for (int index = 0;
            index < CoefficientsPerSubpatch;
            index++)
        {
            matrix[row, offset + index] =
                basis[index];
        }

        rhs[row] = target;
        row++;
        return true;
    }

    private static bool TryAddDirectionalConstraint(
        double[,] matrix,
        double[] rhs,
        ref int row,
        SubtriangleGeometry[] subtriangles,
        int subpatchIndex,
        Double2 point,
        Double2 direction,
        double target,
        out string errorMessage)
    {
        errorMessage = "";

        if (!TryEvaluateBasis(
            subtriangles[subpatchIndex],
            point,
            out _,
            out Double2[] basisGradients,
            out errorMessage))
        {
            return false;
        }

        int offset =
            subpatchIndex * CoefficientsPerSubpatch;

        for (int index = 0;
            index < CoefficientsPerSubpatch;
            index++)
        {
            matrix[row, offset + index] =
                Dot(
                    basisGradients[index],
                    direction);
        }

        rhs[row] = target;
        row++;
        return true;
    }

    private static bool TryAddDifferenceConstraint(
        double[,] matrix,
        double[] rhs,
        ref int row,
        SubtriangleGeometry[] subtriangles,
        int firstSubpatch,
        int secondSubpatch,
        Double2 point,
        bool derivative,
        Double2 direction,
        out string errorMessage)
    {
        errorMessage = "";

        if (!TryEvaluateBasis(
            subtriangles[firstSubpatch],
            point,
            out double[] firstBasis,
            out Double2[] firstGradients,
            out errorMessage) ||
            !TryEvaluateBasis(
                subtriangles[secondSubpatch],
                point,
                out double[] secondBasis,
                out Double2[] secondGradients,
                out errorMessage))
        {
            return false;
        }

        int firstOffset =
            firstSubpatch * CoefficientsPerSubpatch;

        int secondOffset =
            secondSubpatch * CoefficientsPerSubpatch;

        for (int index = 0;
            index < CoefficientsPerSubpatch;
            index++)
        {
            double firstValue =
                derivative
                    ? Dot(firstGradients[index], direction)
                    : firstBasis[index];

            double secondValue =
                derivative
                    ? Dot(secondGradients[index], direction)
                    : secondBasis[index];

            matrix[row, firstOffset + index] =
                firstValue;

            matrix[row, secondOffset + index] =
                -secondValue;
        }

        rhs[row] = 0.0;
        row++;
        return true;
    }

    private static bool TryEvaluateBasis(
        SubtriangleGeometry geometry,
        Double2 point,
        out double[] basis,
        out Double2[] basisGradients,
        out string errorMessage)
    {
        basis = null;
        basisGradients = null;
        errorMessage = "";

        if (!TryCalculateBarycentric(
            geometry,
            point,
            out double u,
            out double v,
            out double w,
            out errorMessage))
        {
            return false;
        }

        double u2 = u * u;
        double v2 = v * v;
        double w2 = w * w;

        basis =
            new double[CoefficientsPerSubpatch]
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

        basisGradients =
            new Double2[CoefficientsPerSubpatch];

        for (int index = 0;
            index < CoefficientsPerSubpatch;
            index++)
        {
            basisGradients[index] =
                geometry.GradientU * du[index] +
                geometry.GradientV * dv[index] +
                geometry.GradientW * dw[index];

            if (
                !IsFinite(basis[index]) ||
                !IsFinite(basisGradients[index]))
            {
                errorMessage =
                    "Smooth Bernstein basis evaluation produced a non-finite value.";
                return false;
            }
        }

        return true;
    }

    private static bool TryCalculateBarycentric(
        SubtriangleGeometry geometry,
        Double2 point,
        out double u,
        out double v,
        out double w,
        out string errorMessage)
    {
        u = 0.0;
        v = 0.0;
        w = 0.0;
        errorMessage = "";

        double dx =
            point.X - geometry.C.X;

        double dz =
            point.Z - geometry.C.Z;

        u =
            geometry.GradientU.X * dx +
            geometry.GradientU.Z * dz;

        v =
            geometry.GradientV.X * dx +
            geometry.GradientV.Z * dz;

        w =
            1.0 - u - v;

        if (
            !IsFinite(u) ||
            !IsFinite(v) ||
            !IsFinite(w))
        {
            errorMessage =
                "Smooth barycentric evaluation produced a non-finite value.";
            return false;
        }

        return true;
    }

    private static bool TryGetCanonicalEdgeNormal(
        int vertexIndexA,
        int vertexIndexB,
        Double2 positionA,
        Double2 positionB,
        out Double2 normal,
        out string errorMessage)
    {
        normal = default(Double2);
        errorMessage = "";

        Double2 canonicalDelta =
            vertexIndexA <= vertexIndexB
                ? positionB - positionA
                : positionA - positionB;

        return TryNormalizePerpendicular(
            canonicalDelta,
            out normal,
            out errorMessage);
    }

    private static bool TryNormalizePerpendicular(
        Double2 vector,
        out Double2 normal,
        out string errorMessage)
    {
        normal = default(Double2);
        errorMessage = "";

        double lengthSquared =
            vector.X * vector.X +
            vector.Z * vector.Z;

        double minimum =
            TerrainNodeElevationGeometryUtility
                .MinimumTriangulationVertexSeparation;

        double minimumSquared =
            minimum * minimum;

        if (
            !IsFinite(lengthSquared) ||
            lengthSquared < minimumSquared)
        {
            errorMessage =
                "Smooth derivative constraint contains an unsafe edge length.";
            return false;
        }

        double inverseLength =
            1.0 / Math.Sqrt(lengthSquared);

        Double2 tangent =
            vector * inverseLength;

        normal =
            new Double2(
                -tangent.Z,
                tangent.X);

        if (!IsFinite(normal))
        {
            errorMessage =
                "Smooth derivative constraint produced a non-finite normal.";
            return false;
        }

        return true;
    }

    private static bool TrySolveLeastSquares(
        double[,] matrix,
        double[] rhs,
        out double[] solution,
        out string errorMessage)
    {
        solution = null;
        errorMessage = "";

        int rowCount =
            matrix.GetLength(0);

        int columnCount =
            matrix.GetLength(1);

        if (
            rowCount != rhs.Length ||
            columnCount != UnknownCount ||
            rowCount < columnCount)
        {
            errorMessage =
                "Smooth HCT local solve received an invalid matrix shape.";
            return false;
        }

        /*
         * Normalize every equation before QR. The HCT system mixes height and
         * derivative constraints, so row normalization improves scale behavior
         * without changing the exact solution of this consistent system.
         */
        double[,] normalized =
            new double[rowCount, columnCount];

        double[] normalizedRhs =
            new double[rowCount];

        for (int row = 0; row < rowCount; row++)
        {
            double normSquared = 0.0;

            for (int column = 0;
                column < columnCount;
                column++)
            {
                double value =
                    matrix[row, column];

                if (!IsFinite(value))
                {
                    errorMessage =
                        "Smooth HCT constraint matrix contains a non-finite value.";
                    return false;
                }

                normSquared +=
                    value * value;
            }

            if (
                !IsFinite(normSquared) ||
                normSquared <= 0.0 ||
                !IsFinite(rhs[row]))
            {
                errorMessage =
                    "Smooth HCT constraint row is singular or non-finite.";
                return false;
            }

            double inverseNorm =
                1.0 / Math.Sqrt(normSquared);

            for (int column = 0;
                column < columnCount;
                column++)
            {
                normalized[row, column] =
                    matrix[row, column] * inverseNorm;
            }

            normalizedRhs[row] =
                rhs[row] * inverseNorm;
        }

        /*
         * Deterministic modified Gram-Schmidt QR with one re-orthogonalization
         * pass. This avoids normal-equation squaring of the local condition
         * number while remaining compact enough for a 33x30 per-triangle solve.
         */
        double[,] q =
            new double[rowCount, columnCount];

        double[,] r =
            new double[columnCount, columnCount];

        double[] work =
            new double[rowCount];

        for (int column = 0;
            column < columnCount;
            column++)
        {
            for (int row = 0; row < rowCount; row++)
            {
                work[row] =
                    normalized[row, column];
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int previous = 0;
                    previous < column;
                    previous++)
                {
                    double projection = 0.0;

                    for (int row = 0; row < rowCount; row++)
                    {
                        projection +=
                            q[row, previous] * work[row];
                    }

                    r[previous, column] +=
                        projection;

                    for (int row = 0; row < rowCount; row++)
                    {
                        work[row] -=
                            projection * q[row, previous];
                    }
                }
            }

            double normSquared = 0.0;

            for (int row = 0; row < rowCount; row++)
            {
                normSquared +=
                    work[row] * work[row];
            }

            if (
                !IsFinite(normSquared) ||
                normSquared <= SolverRankTolerance * SolverRankTolerance)
            {
                errorMessage =
                    "Smooth HCT local system is rank deficient or too poorly " +
                    "conditioned for deterministic construction.";
                return false;
            }

            double norm =
                Math.Sqrt(normSquared);

            r[column, column] =
                norm;

            double inverseNorm =
                1.0 / norm;

            for (int row = 0; row < rowCount; row++)
            {
                q[row, column] =
                    work[row] * inverseNorm;
            }
        }

        double[] projectedRhs =
            new double[columnCount];

        for (int column = 0;
            column < columnCount;
            column++)
        {
            double value = 0.0;

            for (int row = 0; row < rowCount; row++)
            {
                value +=
                    q[row, column] * normalizedRhs[row];
            }

            projectedRhs[column] =
                value;
        }

        double[] solved =
            new double[columnCount];

        for (int row = columnCount - 1;
            row >= 0;
            row--)
        {
            double diagonal =
                r[row, row];

            if (
                !IsFinite(diagonal) ||
                Math.Abs(diagonal) <= SolverRankTolerance)
            {
                errorMessage =
                    "Smooth HCT local solve encountered an unsafe QR pivot.";
                return false;
            }

            double value =
                projectedRhs[row];

            for (int column = row + 1;
                column < columnCount;
                column++)
            {
                value -=
                    r[row, column] * solved[column];
            }

            solved[row] =
                value / diagonal;

            if (!IsFinite(solved[row]))
            {
                errorMessage =
                    "Smooth HCT local solve produced a non-finite coefficient.";
                return false;
            }
        }

        double maximumResidual = 0.0;
        double residualScale = 1.0;

        for (int row = 0; row < rowCount; row++)
        {
            double evaluated = 0.0;

            for (int column = 0;
                column < columnCount;
                column++)
            {
                evaluated +=
                    normalized[row, column] * solved[column];
            }

            double residual =
                Math.Abs(evaluated - normalizedRhs[row]);

            maximumResidual =
                Math.Max(maximumResidual, residual);

            residualScale =
                Math.Max(
                    residualScale,
                    Math.Abs(normalizedRhs[row]));
        }

        if (
            !IsFinite(maximumResidual) ||
            maximumResidual > SolverResidualTolerance * residualScale)
        {
            errorMessage =
                $"Smooth HCT local solve residual {maximumResidual} exceeds " +
                $"the supported tolerance {SolverResidualTolerance * residualScale}.";
            return false;
        }

        solution = solved;
        return true;
    }

    private static bool TryConvertToFiniteFloat(
        double value,
        out float converted)
    {
        converted = 0f;

        if (
            !IsFinite(value) ||
            value > float.MaxValue ||
            value < -float.MaxValue)
        {
            return false;
        }

        converted =
            (float)value;

        return IsFinite(converted);
    }

    private static Double2 ToDouble2(Vector2 value)
    {
        return new Double2(value.x, value.y);
    }

    private static double Dot(Double2 a, Double2 b)
    {
        return
            a.X * b.X +
            a.Z * b.Z;
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
