using UnityEngine;

/*
 * Persistent CPU copy of one terrain-analysis tile.
 *
 * Values are stored as float even though GPU analysis uses RHalf. This keeps
 * CPU consumers simple and makes repeated sampling inexpensive after one
 * asynchronous whole-tile readback.
 */
public sealed class TerrainAnalysisTileData
{
    private readonly float[] values;

    public TerrainAnalysisKey Key { get; }
    public Vector2Int TileCoordinate { get; }
    public int Revision { get; }
    public int SamplesPerSide { get; }
    public float SampleSpacing { get; }
    public Vector2 WorldOriginXZ { get; }

    public int SampleCount =>
        values != null
            ? values.Length
            : 0;

    public TerrainAnalysisTileData(
        TerrainAnalysisTile tile,
        float[] values
    )
    {
        Key = tile.Key;
        TileCoordinate = tile.TileCoordinate;
        Revision = tile.Revision;
        SamplesPerSide = tile.SamplesPerSide;
        SampleSpacing = tile.SampleSpacing;
        WorldOriginXZ = tile.WorldOriginXZ;

        this.values =
            values ??
            System.Array.Empty<float>();
    }

    public bool TryGetSample(
        int sampleX,
        int sampleZ,
        out float value
    )
    {
        value = 0f;

        if (
            sampleX < 0 ||
            sampleZ < 0 ||
            sampleX >= SamplesPerSide ||
            sampleZ >= SamplesPerSide
        )
        {
            return false;
        }

        int index =
            sampleX +
            sampleZ *
            SamplesPerSide;

        if (
            index < 0 ||
            index >= values.Length
        )
        {
            return false;
        }

        value =
            values[index];

        return true;
    }

    public bool TrySampleBilinear(
        Vector2 worldXZ,
        out float value
    )
    {
        value = 0f;

        if (
            SamplesPerSide <= 1 ||
            SampleSpacing <= 0f ||
            values.Length !=
                SamplesPerSide *
                SamplesPerSide
        )
        {
            return false;
        }

        float tileWorldSize =
            (SamplesPerSide - 1) *
            SampleSpacing;

        const float Tolerance =
            0.0001f;

        if (
            worldXZ.x <
                WorldOriginXZ.x -
                Tolerance ||
            worldXZ.y <
                WorldOriginXZ.y -
                Tolerance ||
            worldXZ.x >
                WorldOriginXZ.x +
                tileWorldSize +
                Tolerance ||
            worldXZ.y >
                WorldOriginXZ.y +
                tileWorldSize +
                Tolerance
        )
        {
            return false;
        }

        Vector2 sample =
            (
                worldXZ -
                WorldOriginXZ
            )
            /
            SampleSpacing;

        float maxSample =
            SamplesPerSide - 1;

        sample.x =
            Mathf.Clamp(
                sample.x,
                0f,
                maxSample
            );

        sample.y =
            Mathf.Clamp(
                sample.y,
                0f,
                maxSample
            );

        int x0 =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    sample.x
                ),
                0,
                SamplesPerSide - 1
            );

        int z0 =
            Mathf.Clamp(
                Mathf.FloorToInt(
                    sample.y
                ),
                0,
                SamplesPerSide - 1
            );

        int x1 =
            Mathf.Min(
                x0 + 1,
                SamplesPerSide - 1
            );

        int z1 =
            Mathf.Min(
                z0 + 1,
                SamplesPerSide - 1
            );

        float tx =
            sample.x -
            x0;

        float tz =
            sample.y -
            z0;

        float v00 =
            values[
                x0 +
                z0 *
                SamplesPerSide
            ];

        float v10 =
            values[
                x1 +
                z0 *
                SamplesPerSide
            ];

        float v01 =
            values[
                x0 +
                z1 *
                SamplesPerSide
            ];

        float v11 =
            values[
                x1 +
                z1 *
                SamplesPerSide
            ];

        float a =
            Mathf.Lerp(
                v00,
                v10,
                tx
            );

        float b =
            Mathf.Lerp(
                v01,
                v11,
                tx
            );

        value =
            Mathf.Lerp(
                a,
                b,
                tz
            );

        return true;
    }

    /*
     * Borrow the tile's backing storage for synchronous read-only consumers.
     * The returned array remains owned by this TerrainAnalysisTileData. The
     * caller must not modify it or retain it beyond this object's lifetime.
     * Consumers that require independent ownership must use CopyValues().
     */
    internal float[] BorrowValuesForReadOnlyAccess()
    {
        return values;
    }

    public float[] CopyValues()
    {
        float[] copy =
            new float[
                values.Length
            ];

        System.Array.Copy(
            values,
            copy,
            values.Length
        );

        return copy;
    }
}
