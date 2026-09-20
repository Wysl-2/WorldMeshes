using System;
using System.Collections.Generic;
using UnityEngine;

internal enum TerrainAuthoringPreviewTransitionState
{
    Planned,
    Preparing,
    CopyingRetained,
    LoadingSourceTiles,
    ComposingSourceTiles,
    Finalizing,
    ReadyToActivate,
    Activating,
    Activated,
    Cancelled,
    Failed
}

/*
 * Immutable world-tile classification plus mutable execution diagnostics for
 * one edit-mode resident-cache replacement.
 *
 * World coordinates remain authoritative throughout the transaction. Cache
 * slice indices are deliberately derived only at the point where a source or
 * destination cache operation is performed.
 */
internal sealed class TerrainAuthoringPreviewCacheTransition
{
    private readonly List<Vector2Int> retainedTiles =
        new List<Vector2Int>();

    private readonly List<Vector2Int> enteringTiles =
        new List<Vector2Int>();

    private readonly List<Vector2Int> leavingTiles =
        new List<Vector2Int>();

    private readonly List<Vector2Int> reusableRetainedTiles =
        new List<Vector2Int>();

    private readonly List<Vector2Int> sourceMaterializationTiles =
        new List<Vector2Int>();

    public bool HasSourceWindow { get; }

    public TerrainHeightCacheWindow SourceWindow { get; }

    public TerrainHeightCacheWindow TargetWindow { get; }

    public IReadOnlyList<Vector2Int> RetainedTiles =>
        retainedTiles;

    public IReadOnlyList<Vector2Int> EnteringTiles =>
        enteringTiles;

    public IReadOnlyList<Vector2Int> LeavingTiles =>
        leavingTiles;

    public IReadOnlyList<Vector2Int> ReusableRetainedTiles =>
        reusableRetainedTiles;

    public IReadOnlyList<Vector2Int> SourceMaterializationTiles =>
        sourceMaterializationTiles;

    public TerrainAuthoringPreviewTransitionState State { get; private set; }

    public string FailureMessage { get; private set; } =
        "";

    public string TargetCommittedHeightfieldSignature { get; }

    public string TargetOverallAuthoringSignature { get; }

    public int SourceTextureInstanceId { get; internal set; }

    public int DestinationTextureInstanceId { get; internal set; }

    public int RetainedGpuCopyCount { get; internal set; }

    public int CommittedSourceLoadCount { get; internal set; }

    public int FullyComposedTileCount { get; internal set; }

    public long RequestGeneration { get; internal set; }

    public long TargetAuthoringGeneration { get; internal set; }

    public bool CommittedRebuildRequested { get; internal set; }

    public int RetainedCopyCursor { get; internal set; }

    public int CommittedLoadCursor { get; internal set; }

    public int CompositionCursor { get; internal set; }

    public int TotalWorkUnits { get; internal set; }

    public int CompletedWorkUnits { get; internal set; }

    public string CancellationReason { get; private set; } =
        "";

    private TerrainAuthoringPreviewCacheTransition(
        bool hasSourceWindow,
        TerrainHeightCacheWindow sourceWindow,
        TerrainHeightCacheWindow targetWindow,
        string targetCommittedHeightfieldSignature,
        string targetOverallAuthoringSignature
    )
    {
        HasSourceWindow =
            hasSourceWindow;

        SourceWindow =
            sourceWindow;

        TargetWindow =
            targetWindow;

        TargetCommittedHeightfieldSignature =
            targetCommittedHeightfieldSignature ?? "";

        TargetOverallAuthoringSignature =
            targetOverallAuthoringSignature ?? "";

        State =
            TerrainAuthoringPreviewTransitionState.Planned;

        BuildClassification();
    }

    public static bool TryCreate(
        bool hasSourceWindow,
        TerrainHeightCacheWindow sourceWindow,
        TerrainHeightCacheWindow targetWindow,
        string targetCommittedHeightfieldSignature,
        string targetOverallAuthoringSignature,
        out TerrainAuthoringPreviewCacheTransition transition,
        out string errorMessage
    )
    {
        transition =
            null;

        errorMessage =
            "";

        if (!targetWindow.IsValid)
        {
            errorMessage =
                "The staged transition target window is invalid.";

            return false;
        }

        if (
            hasSourceWindow
            &&
            !sourceWindow.IsValid
        )
        {
            errorMessage =
                "The staged transition source window is invalid.";

            return false;
        }

        transition =
            new TerrainAuthoringPreviewCacheTransition(
                hasSourceWindow,
                sourceWindow,
                targetWindow,
                targetCommittedHeightfieldSignature,
                targetOverallAuthoringSignature
            );

        return true;
    }

    internal void SetState(
        TerrainAuthoringPreviewTransitionState state
    )
    {
        State =
            state;
    }

    internal void AddReusableRetainedTile(
        Vector2Int tile
    )
    {
        reusableRetainedTiles.Add(
            tile
        );
    }

    internal void AddSourceMaterializationTile(
        Vector2Int tile
    )
    {
        if (
            !sourceMaterializationTiles.Contains(
                tile
            )
        )
        {
            sourceMaterializationTiles.Add(
                tile
            );
        }
    }

    internal void MarkFailed(
        string failureMessage
    )
    {
        FailureMessage =
            string.IsNullOrEmpty(
                failureMessage
            )
                ? "The staged height-cache transition failed."
                : failureMessage;

        State =
            TerrainAuthoringPreviewTransitionState.Failed;
    }

    internal void MarkCancelled(
        string cancellationReason
    )
    {
        CancellationReason =
            string.IsNullOrEmpty(
                cancellationReason
            )
                ? "The staged height-cache transition was cancelled."
                : cancellationReason;

        State =
            TerrainAuthoringPreviewTransitionState.Cancelled;
    }

    internal void ResetStreamingExecutionState()
    {
        RetainedCopyCursor =
            0;

        CommittedLoadCursor =
            0;

        CompositionCursor =
            0;

        RetainedGpuCopyCount =
            0;

        CommittedSourceLoadCount =
            0;

        FullyComposedTileCount =
            0;

        TotalWorkUnits =
            0;

        CompletedWorkUnits =
            0;

        CancellationReason =
            "";
    }

    private void BuildClassification()
    {
        if (!HasSourceWindow)
        {
            AppendWindowTiles(
                TargetWindow,
                enteringTiles
            );

            return;
        }

        bool hasIntersection =
            SourceWindow.TryGetIntersection(
                TargetWindow,
                out TerrainHeightCacheWindow overlap
            );

        for (
            int z = TargetWindow.OriginTile.y;
            z < TargetWindow.MaximumExclusive.y;
            z++
        )
        {
            for (
                int x = TargetWindow.OriginTile.x;
                x < TargetWindow.MaximumExclusive.x;
                x++
            )
            {
                Vector2Int tile =
                    new Vector2Int(
                        x,
                        z
                    );

                if (
                    hasIntersection
                    &&
                    overlap.Contains(
                        tile
                    )
                )
                {
                    retainedTiles.Add(
                        tile
                    );
                }
                else
                {
                    enteringTiles.Add(
                        tile
                    );
                }
            }
        }

        for (
            int z = SourceWindow.OriginTile.y;
            z < SourceWindow.MaximumExclusive.y;
            z++
        )
        {
            for (
                int x = SourceWindow.OriginTile.x;
                x < SourceWindow.MaximumExclusive.x;
                x++
            )
            {
                Vector2Int tile =
                    new Vector2Int(
                        x,
                        z
                    );

                if (
                    !TargetWindow.Contains(
                        tile
                    )
                )
                {
                    leavingTiles.Add(
                        tile
                    );
                }
            }
        }
    }

    private static void AppendWindowTiles(
        TerrainHeightCacheWindow window,
        List<Vector2Int> destination
    )
    {
        for (
            int z = window.OriginTile.y;
            z < window.MaximumExclusive.y;
            z++
        )
        {
            for (
                int x = window.OriginTile.x;
                x < window.MaximumExclusive.x;
                x++
            )
            {
                destination.Add(
                    new Vector2Int(
                        x,
                        z
                    )
                );
            }
        }
    }
}
