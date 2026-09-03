using System;

/*
 * Identifies one exact terrain-analysis field.
 *
 * Scale-independent:
 *
 *     Slope
 *
 * Scale-dependent:
 *
 *     Curvature(8 m)
 *     Roughness(16 m)
 *     LocalRelief(32 m)
 *
 * Scale is quantized to centimetres before becoming part of the key.
 * This avoids raw floating-point values creating duplicate cache entries
 * for effectively identical analysis scales.
 */
public readonly struct TerrainAnalysisKey :
    IEquatable<TerrainAnalysisKey>
{
    private const int ScaleUnitsPerMeter =
        100;

    public TerrainAnalysisType Type
    {
        get;
    }

    public int ScaleCentimeters
    {
        get;
    }

    public bool HasScale
    {
        get
        {
            return
                ScaleCentimeters >
                0;
        }
    }

    public float ScaleMeters
    {
        get
        {
            return
                ScaleCentimeters /
                (float)ScaleUnitsPerMeter;
        }
    }

    public TerrainAnalysisKey(
        TerrainAnalysisType type
    )
    {
        Type =
            type;

        ScaleCentimeters =
            0;
    }

    public TerrainAnalysisKey(
        TerrainAnalysisType type,
        float scaleMeters
    )
    {
        Type =
            type;

        ScaleCentimeters =
            QuantizeScaleCentimeters(
                scaleMeters
            );
    }

    public static TerrainAnalysisKey Slope
    {
        get
        {
            return
                new TerrainAnalysisKey(
                    TerrainAnalysisType.Slope
                );
        }
    }

    public static TerrainAnalysisKey Curvature(
        float scaleMeters
    )
    {
        return
            new TerrainAnalysisKey(
                TerrainAnalysisType.Curvature,
                scaleMeters
            );
    }

    public static TerrainAnalysisKey Roughness(
        float scaleMeters
    )
    {
        return
            new TerrainAnalysisKey(
                TerrainAnalysisType.Roughness,
                scaleMeters
            );
    }

    public static TerrainAnalysisKey LocalRelief(
        float scaleMeters
    )
    {
        return
            new TerrainAnalysisKey(
                TerrainAnalysisType.LocalRelief,
                scaleMeters
            );
    }

    public bool Equals(
        TerrainAnalysisKey other
    )
    {
        return
            Type ==
                other.Type
            &&
            ScaleCentimeters ==
                other.ScaleCentimeters;
    }

    public override bool Equals(
        object obj
    )
    {
        return
            obj is TerrainAnalysisKey
            &&
            Equals(
                (TerrainAnalysisKey)obj
            );
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return
                (
                    (int)Type *
                    397
                )
                ^
                ScaleCentimeters;
        }
    }

    public static bool operator ==(
        TerrainAnalysisKey left,
        TerrainAnalysisKey right
    )
    {
        return
            left.Equals(
                right
            );
    }

    public static bool operator !=(
        TerrainAnalysisKey left,
        TerrainAnalysisKey right
    )
    {
        return
            !left.Equals(
                right
            );
    }

    public override string ToString()
    {
        if (!HasScale)
        {
            return
                Type.ToString();
        }

        return
            Type +
            "(" +
            ScaleMeters.ToString(
                "0.##"
            ) +
            " m)";
    }

    private static int QuantizeScaleCentimeters(
        float scaleMeters
    )
    {
        if (
            float.IsNaN(
                scaleMeters
            )
            ||
            float.IsInfinity(
                scaleMeters
            )
            ||
            scaleMeters <=
                0f
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleMeters),
                scaleMeters,
                "Terrain analysis scale must be a finite value greater than zero."
            );
        }

        double scaledValue =
            (double)scaleMeters *
            ScaleUnitsPerMeter;

        if (
            scaledValue >
            int.MaxValue
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleMeters),
                scaleMeters,
                "Terrain analysis scale is too large."
            );
        }

        int quantized =
            (int)Math.Round(
                scaledValue,
                MidpointRounding.AwayFromZero
            );

        return
            Math.Max(
                1,
                quantized
            );
    }
}
