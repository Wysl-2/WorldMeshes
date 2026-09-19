using System;
using UnityEngine;

/*
 * Immutable rectangular window of world height-tile coordinates.
 *
 * OriginTile is inclusive. MaximumExclusive is OriginTile + Size.
 * Persistent terrain identity always uses world tile coordinates; GPU cache
 * slices are derived separately from cache-local coordinates.
 */
public readonly struct TerrainHeightCacheWindow :
    IEquatable<TerrainHeightCacheWindow>
{
    public Vector2Int OriginTile { get; }

    public Vector2Int Size { get; }

    public int Width =>
        Size.x;

    public int Height =>
        Size.y;

    public int TileCount
    {
        get
        {
            if (!IsValid)
            {
                return 0;
            }

            long count =
                (long)Width *
                Height;

            return
                count > int.MaxValue
                    ? int.MaxValue
                    : (int)count;
        }
    }

    public Vector2Int MaximumExclusive =>
        OriginTile +
        Size;

    public bool IsValid =>
        Width > 0
        &&
        Height > 0;

    public TerrainHeightCacheWindow(
        Vector2Int originTile,
        Vector2Int size
    )
    {
        OriginTile =
            originTile;

        Size =
            size;
    }

    public bool Contains(
        Vector2Int tileCoordinate
    )
    {
        if (!IsValid)
        {
            return false;
        }

        Vector2Int maximumExclusive =
            MaximumExclusive;

        return
            tileCoordinate.x >=
                OriginTile.x
            &&
            tileCoordinate.y >=
                OriginTile.y
            &&
            tileCoordinate.x <
                maximumExclusive.x
            &&
            tileCoordinate.y <
                maximumExclusive.y;
    }

    public bool Contains(
        TerrainHeightCacheWindow other
    )
    {
        if (
            !IsValid
            ||
            !other.IsValid
        )
        {
            return false;
        }

        Vector2Int maximumExclusive =
            MaximumExclusive;

        Vector2Int otherMaximumExclusive =
            other.MaximumExclusive;

        return
            other.OriginTile.x >=
                OriginTile.x
            &&
            other.OriginTile.y >=
                OriginTile.y
            &&
            otherMaximumExclusive.x <=
                maximumExclusive.x
            &&
            otherMaximumExclusive.y <=
                maximumExclusive.y;
    }

    public bool Overlaps(
        TerrainHeightCacheWindow other
    )
    {
        if (
            !IsValid
            ||
            !other.IsValid
        )
        {
            return false;
        }

        Vector2Int maximumExclusive =
            MaximumExclusive;

        Vector2Int otherMaximumExclusive =
            other.MaximumExclusive;

        return
            OriginTile.x <
                otherMaximumExclusive.x
            &&
            maximumExclusive.x >
                other.OriginTile.x
            &&
            OriginTile.y <
                otherMaximumExclusive.y
            &&
            maximumExclusive.y >
                other.OriginTile.y;
    }

    public bool TryGetIntersection(
        TerrainHeightCacheWindow other,
        out TerrainHeightCacheWindow intersection
    )
    {
        intersection =
            default;

        if (!Overlaps(other))
        {
            return false;
        }

        Vector2Int maximumExclusive =
            MaximumExclusive;

        Vector2Int otherMaximumExclusive =
            other.MaximumExclusive;

        Vector2Int intersectionOrigin =
            new Vector2Int(
                Mathf.Max(
                    OriginTile.x,
                    other.OriginTile.x
                ),
                Mathf.Max(
                    OriginTile.y,
                    other.OriginTile.y
                )
            );

        Vector2Int intersectionMaximumExclusive =
            new Vector2Int(
                Mathf.Min(
                    maximumExclusive.x,
                    otherMaximumExclusive.x
                ),
                Mathf.Min(
                    maximumExclusive.y,
                    otherMaximumExclusive.y
                )
            );

        intersection =
            new TerrainHeightCacheWindow(
                intersectionOrigin,
                intersectionMaximumExclusive -
                    intersectionOrigin
            );

        return
            intersection.IsValid;
    }

    public static bool TryFitToWorld(
        Vector2Int desiredOriginTile,
        Vector2Int requestedSize,
        Vector2Int worldGridSize,
        out TerrainHeightCacheWindow fittedWindow
    )
    {
        fittedWindow =
            default;

        if (
            requestedSize.x <= 0
            ||
            requestedSize.y <= 0
            ||
            worldGridSize.x <= 0
            ||
            worldGridSize.y <= 0
        )
        {
            return false;
        }

        Vector2Int fittedSize =
            new Vector2Int(
                Mathf.Min(
                    requestedSize.x,
                    worldGridSize.x
                ),
                Mathf.Min(
                    requestedSize.y,
                    worldGridSize.y
                )
            );

        Vector2Int maximumOrigin =
            worldGridSize -
            fittedSize;

        Vector2Int fittedOrigin =
            new Vector2Int(
                Mathf.Clamp(
                    desiredOriginTile.x,
                    0,
                    maximumOrigin.x
                ),
                Mathf.Clamp(
                    desiredOriginTile.y,
                    0,
                    maximumOrigin.y
                )
            );

        fittedWindow =
            new TerrainHeightCacheWindow(
                fittedOrigin,
                fittedSize
            );

        return true;
    }

    public bool Equals(
        TerrainHeightCacheWindow other
    )
    {
        return
            OriginTile ==
                other.OriginTile
            &&
            Size ==
                other.Size;
    }

    public override bool Equals(
        object obj
    )
    {
        return
            obj is TerrainHeightCacheWindow other
            &&
            Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return
                (OriginTile.GetHashCode() * 397)
                ^
                Size.GetHashCode();
        }
    }

    public static bool operator ==(
        TerrainHeightCacheWindow left,
        TerrainHeightCacheWindow right
    )
    {
        return
            left.Equals(
                right
            );
    }

    public static bool operator !=(
        TerrainHeightCacheWindow left,
        TerrainHeightCacheWindow right
    )
    {
        return
            !left.Equals(
                right
            );
    }

    public override string ToString()
    {
        return
            $"Origin={OriginTile}, Size={Size}";
    }
}
