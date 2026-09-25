using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

public enum TerrainHeightLodTransitionState
{
    Idle,
    WaitingForRequiredPages,
    PopulatingStaging,
    CommitPending
}

public enum TerrainHeightResidentPageState
{
    Loaded,
    Failed
}

public readonly struct TerrainHeightPageKey :
    System.IEquatable<TerrainHeightPageKey>
{
    public int SampleStride { get; }
    public Vector2Int Coordinate { get; }

    public TerrainHeightPageKey(
        int sampleStride,
        Vector2Int coordinate
    )
    {
        SampleStride = sampleStride;
        Coordinate = coordinate;
    }

    public bool Equals(TerrainHeightPageKey other)
    {
        return
            SampleStride == other.SampleStride
            && Coordinate == other.Coordinate;
    }

    public override bool Equals(object obj)
    {
        return
            obj is TerrainHeightPageKey other
            && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return SampleStride * 397 ^ Coordinate.GetHashCode();
        }
    }
}

/*
 * Retained temporarily for compatibility with legacy helper code.
 * Production source loading no longer populates ResidentPages; Addressable
 * source ownership lives in TerrainHeightPageLoadScheduler.
 */
internal sealed class TerrainHeightResidentPage
{
    public Vector2Int Coordinate;
    public int SampleStride;
    public string Address;
    public AsyncOperationHandle<Texture2D> Handle;
    public Texture2D Texture;
    public int LastRequestGeneration;
    public TerrainHeightResidentPageState State;
}

internal sealed class TerrainHeightLodRuntimeState
{
    public int Level { get; }
    public int SampleStride { get; }
    public TerrainHeightStreamingLevelDescriptor Descriptor { get; }

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

    public TerrainHeightLodTransitionState TransitionState =
        TerrainHeightLodTransitionState.Idle;

    public int RequestGeneration;
    public int StagingGeneration;

    public readonly Dictionary<Vector2Int, TerrainHeightResidentPage> ResidentPages =
        new Dictionary<Vector2Int, TerrainHeightResidentPage>();

    public readonly HashSet<Vector2Int> ActiveValidPages =
        new HashSet<Vector2Int>();

    public readonly HashSet<Vector2Int> StagingValidPages =
        new HashSet<Vector2Int>();

    public TerrainHeightLodRuntimeState(
        int level,
        int sampleStride,
        TerrainHeightStreamingLevelDescriptor descriptor
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
            if (CacheWidth <= 0 || CacheHeight <= 0)
            {
                return default;
            }

            return new TerrainHeightPageRect(
                ActiveCacheOrigin,
                new Vector2Int(
                    ActiveCacheOrigin.x + CacheWidth - 1,
                    ActiveCacheOrigin.y + CacheHeight - 1
                )
            );
        }
    }

    public TerrainHeightPageRect StagingCachePages
    {
        get
        {
            if (CacheWidth <= 0 || CacheHeight <= 0)
            {
                return default;
            }

            return new TerrainHeightPageRect(
                StagingCacheOrigin,
                new Vector2Int(
                    StagingCacheOrigin.x + CacheWidth - 1,
                    StagingCacheOrigin.y + CacheHeight - 1
                )
            );
        }
    }

    public bool HasLoadedPage(Vector2Int coordinate)
    {
        return
            ResidentPages.TryGetValue(
                coordinate,
                out TerrainHeightResidentPage page
            )
            && page != null
            && page.State == TerrainHeightResidentPageState.Loaded
            && page.Texture != null;
    }
}
