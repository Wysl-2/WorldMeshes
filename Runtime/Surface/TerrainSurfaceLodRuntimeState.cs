using System.Collections.Generic;
using UnityEngine;

internal readonly struct TerrainSurfacePageKey :
    System.IEquatable<TerrainSurfacePageKey>
{
    public int SampleStride { get; }
    public Vector2Int Coordinate { get; }

    public TerrainSurfacePageKey(
        int sampleStride,
        Vector2Int coordinate
    )
    {
        SampleStride = sampleStride;
        Coordinate = coordinate;
    }

    public bool Equals(
        TerrainSurfacePageKey other
    )
    {
        return
            SampleStride == other.SampleStride
            &&
            Coordinate == other.Coordinate;
    }

    public override bool Equals(
        object obj
    )
    {
        return
            obj is TerrainSurfacePageKey other
            &&
            Equals(
                other
            );
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return
                SampleStride * 397
                ^
                Coordinate.GetHashCode();
        }
    }
}

/*
 * Durable runtime state for one clipmap Surface LOD.
 *
 * Addressable source textures are transient transport resources owned by
 * TerrainSurfacePageLoadScheduler. This state owns only cache geometry,
 * transition metadata, GPU cache references, and page-validity sets.
 */
internal sealed class TerrainSurfaceLodRuntimeState
{
    public int Level { get; }
    public int SampleStride { get; }

    public TerrainSurfaceStreamingLevelDescriptor
        Descriptor { get; }

    public int CacheWidth;
    public int CacheHeight;

    public Vector2Int ActiveCacheOrigin;
    public Vector2Int RequestedCacheOrigin;
    public Vector2Int StagingCacheOrigin;

    public TerrainHeightPageRect ActiveRequiredPages;
    public TerrainHeightPageRect RequestedRequiredPages;
    public TerrainHeightPageRect RequestedPrefetchPages;
    public TerrainHeightPageRect StagingRequiredPages;

    public Texture2DArray ActiveCache;
    public Texture2DArray StagingCache;

    public bool CacheReady;

    public TerrainSurfaceCacheTransitionState
        TransitionState =
            TerrainSurfaceCacheTransitionState.Idle;

    public int RequestGeneration;
    public int StagingGeneration;

    public readonly HashSet<Vector2Int>
        ActiveValidPages =
            new HashSet<Vector2Int>();

    public readonly HashSet<Vector2Int>
        StagingValidPages =
            new HashSet<Vector2Int>();

    public TerrainSurfaceLodRuntimeState(
        int level,
        int sampleStride,
        TerrainSurfaceStreamingLevelDescriptor descriptor
    )
    {
        Level = level;
        SampleStride = sampleStride;
        Descriptor = descriptor;
    }

    public TerrainHeightPageRect ActiveCachePages
    {
        get
        {
            if (
                CacheWidth <= 0
                ||
                CacheHeight <= 0
            )
            {
                return default;
            }

            return
                new TerrainHeightPageRect(
                    ActiveCacheOrigin,
                    new Vector2Int(
                        ActiveCacheOrigin.x +
                            CacheWidth -
                            1,
                        ActiveCacheOrigin.y +
                            CacheHeight -
                            1
                    )
                );
        }
    }

    public TerrainHeightPageRect StagingCachePages
    {
        get
        {
            if (
                CacheWidth <= 0
                ||
                CacheHeight <= 0
            )
            {
                return default;
            }

            return
                new TerrainHeightPageRect(
                    StagingCacheOrigin,
                    new Vector2Int(
                        StagingCacheOrigin.x +
                            CacheWidth -
                            1,
                        StagingCacheOrigin.y +
                            CacheHeight -
                            1
                    )
                );
        }
    }
}
