using UnityEngine;

/*
 * Inclusive geographic runtime-height page rectangle.
 *
 * The multiresolution pyramid keeps the same geographic page footprint at
 * every stride, so this type deliberately contains no resolution metadata.
 */
public readonly struct TerrainHeightPageRect
{
    private readonly bool valid;

    public Vector2Int Minimum { get; }
    public Vector2Int Maximum { get; }

    public int Width =>
        IsValid
            ? Maximum.x - Minimum.x + 1
            : 0;

    public int Height =>
        IsValid
            ? Maximum.y - Minimum.y + 1
            : 0;

    public bool IsValid =>
        valid
        && Maximum.x >= Minimum.x
        && Maximum.y >= Minimum.y;

    public TerrainHeightPageRect(
        Vector2Int minimum,
        Vector2Int maximum
    )
    {
        Minimum = minimum;
        Maximum = maximum;
        valid =
            maximum.x >= minimum.x
            && maximum.y >= minimum.y;
    }

    public bool Contains(
        Vector2Int coordinate
    )
    {
        return
            IsValid
            && coordinate.x >= Minimum.x
            && coordinate.y >= Minimum.y
            && coordinate.x <= Maximum.x
            && coordinate.y <= Maximum.y;
    }

    public bool Contains(
        TerrainHeightPageRect other
    )
    {
        return
            IsValid
            && other.IsValid
            && Contains(other.Minimum)
            && Contains(other.Maximum);
    }

    public TerrainHeightPageRect Expand(
        int pageCount,
        int gridWidth,
        int gridHeight
    )
    {
        if (!IsValid)
        {
            return this;
        }

        int safePageCount =
            Mathf.Max(
                0,
                pageCount
            );

        int maximumX =
            Mathf.Max(
                0,
                gridWidth - 1
            );

        int maximumY =
            Mathf.Max(
                0,
                gridHeight - 1
            );

        return
            new TerrainHeightPageRect(
                new Vector2Int(
                    Mathf.Clamp(
                        Minimum.x - safePageCount,
                        0,
                        maximumX
                    ),
                    Mathf.Clamp(
                        Minimum.y - safePageCount,
                        0,
                        maximumY
                    )
                ),
                new Vector2Int(
                    Mathf.Clamp(
                        Maximum.x + safePageCount,
                        0,
                        maximumX
                    ),
                    Mathf.Clamp(
                        Maximum.y + safePageCount,
                        0,
                        maximumY
                    )
                )
            );
    }
}
