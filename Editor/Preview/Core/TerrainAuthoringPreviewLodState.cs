using System;
using UnityEngine;

internal enum TerrainAuthoringPreviewLodTransitionState
{
    Idle,
    WaitingForRequiredPages,
    PopulatingStaging,
    CommitPending
}

/*
 * Editor-owned state for one clipmap Height LOD.
 *
 * The state owns representation identity, cache references, residency
 * metadata, and transition generations. TerrainAuthoringPreviewService remains
 * responsible for planning, scheduling, Scene View intent, and activation.
 */
internal sealed class TerrainAuthoringPreviewLodState :
    IDisposable
{
    public int Level { get; }

    public int SampleStride { get; }

    public int SamplesPerSide { get; }

    public float SampleSpacing { get; }

    public TerrainHeightCacheWindow ActiveRequiredWindow;

    public TerrainHeightCacheWindow RequestedRequiredWindow;

    public TerrainHeightCacheWindow RequestedDesiredWindow;

    public TerrainAuthoringPreviewCache ActiveCache;

    public TerrainAuthoringPreviewCache StagingCache;

    public bool CacheReady;

    public TerrainAuthoringPreviewLodTransitionState TransitionState =
        TerrainAuthoringPreviewLodTransitionState.Idle;

    public int RequestGeneration;

    public int StagingGeneration;

    public TerrainAuthoringPreviewLodState(
        int level,
        int sampleStride,
        int samplesPerSide,
        float sampleSpacing
    )
    {
        if (
            level < 0
            ||
            level >=
                TerrainClipmapTopologyUtility
                    .MaximumLevelCount
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(level)
            );
        }

        if (
            sampleStride < 1
            ||
            !TerrainHeightResolutionUtility
                .IsPowerOfTwo(
                    sampleStride
                )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleStride)
            );
        }

        if (samplesPerSide < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samplesPerSide)
            );
        }

        if (
            !IsFinite(
                sampleSpacing
            )
            ||
            sampleSpacing <= 0f
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleSpacing)
            );
        }

        Level =
            level;

        SampleStride =
            sampleStride;

        SamplesPerSide =
            samplesPerSide;

        SampleSpacing =
            sampleSpacing;
    }

    public void Dispose()
    {
        TerrainAuthoringPreviewCache active =
            ActiveCache;

        TerrainAuthoringPreviewCache staging =
            StagingCache;

        ActiveCache =
            null;

        StagingCache =
            null;

        if (active != null)
        {
            active.Dispose();
        }

        if (
            staging != null
            &&
            !ReferenceEquals(
                staging,
                active
            )
        )
        {
            staging.Dispose();
        }

        ActiveRequiredWindow =
            default;

        RequestedRequiredWindow =
            default;

        RequestedDesiredWindow =
            default;

        CacheReady =
            false;

        TransitionState =
            TerrainAuthoringPreviewLodTransitionState.Idle;

        RequestGeneration =
            0;

        StagingGeneration =
            0;
    }

    private static bool IsFinite(
        float value
    )
    {
        return
            !float.IsNaN(
                value
            )
            &&
            !float.IsInfinity(
                value
            );
    }
}
