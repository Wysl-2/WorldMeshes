using System;
using UnityEditor;
using UnityEngine;

internal sealed class WorldGridViewportHeightCacheBoundsProvider
{
    private TerrainHeightmapStreamer runtimeStreamer;

    private bool hasExecutionState;

    private WorldGridViewportExecutionState executionState;

    internal event Action BoundsChanged;

    internal WorldGridViewportHeightCacheBoundsProvider()
    {
        TerrainAuthoringPreviewService
            .HeightCacheCoverageChanged +=
                HandleEditorCoverageChanged;

        EditorApplication.playModeStateChanged +=
            HandlePlayModeStateChanged;

        EditorApplication.hierarchyChanged +=
            HandleHierarchyChanged;
    }

    internal bool TryGetBounds(
        WorldGridViewportContext context,
        out Vector2 minimumXZ,
        out Vector2 maximumXZ
    )
    {
        minimumXZ =
            Vector2.zero;

        maximumXZ =
            Vector2.zero;

        EnsureExecutionState(
            context.ExecutionState
        );

        switch (context.ExecutionState)
        {
            case WorldGridViewportExecutionState.EditMode:
                return
                    TerrainAuthoringPreviewService
                        .TryGetHeightCacheWorldCoverage(
                            out minimumXZ,
                            out maximumXZ
                        );

            case WorldGridViewportExecutionState.PlayMode:
                if (!EnsureRuntimeStreamer())
                {
                    return false;
                }

                return
                    runtimeStreamer
                        .TryGetActiveCacheWorldCoverage(
                            out minimumXZ,
                            out maximumXZ
                        );

            default:
                return false;
        }
    }

    private void EnsureExecutionState(
        WorldGridViewportExecutionState currentState
    )
    {
        if (
            hasExecutionState
            &&
            executionState ==
                currentState
        )
        {
            return;
        }

        executionState =
            currentState;

        hasExecutionState =
            true;

        if (
            currentState !=
            WorldGridViewportExecutionState.PlayMode
        )
        {
            ClearRuntimeStreamer();
        }
    }

    private bool EnsureRuntimeStreamer()
    {
        if (runtimeStreamer != null)
        {
            return true;
        }

        if (
            !TerrainWorldSceneUtility
                .TryFindActiveClipmapRoot(
                    out Transform clipmapRoot,
                    out _
                )
            ||
            clipmapRoot == null
        )
        {
            return false;
        }

        TerrainHeightmapStreamer streamer =
            clipmapRoot
                .GetComponent<TerrainHeightmapStreamer>();

        if (streamer == null)
        {
            return false;
        }

        SetRuntimeStreamer(
            streamer
        );

        return true;
    }

    private void SetRuntimeStreamer(
        TerrainHeightmapStreamer streamer
    )
    {
        if (
            runtimeStreamer ==
            streamer
        )
        {
            return;
        }

        ClearRuntimeStreamer();

        runtimeStreamer =
            streamer;

        if (runtimeStreamer != null)
        {
            runtimeStreamer
                .ActiveCacheCoverageChanged +=
                    HandleRuntimeCoverageChanged;
        }
    }

    private void ClearRuntimeStreamer()
    {
        if (runtimeStreamer != null)
        {
            runtimeStreamer
                .ActiveCacheCoverageChanged -=
                    HandleRuntimeCoverageChanged;
        }

        runtimeStreamer =
            null;
    }

    private void HandleEditorCoverageChanged()
    {
        if (
            hasExecutionState
            &&
            executionState ==
                WorldGridViewportExecutionState.EditMode
        )
        {
            BoundsChanged?.Invoke();
        }
    }

    private void HandleRuntimeCoverageChanged()
    {
        if (
            hasExecutionState
            &&
            executionState ==
                WorldGridViewportExecutionState.PlayMode
        )
        {
            BoundsChanged?.Invoke();
        }
    }

    private void HandlePlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        _ =
            state;

        hasExecutionState =
            false;

        ClearRuntimeStreamer();
    }

    private void HandleHierarchyChanged()
    {
        if (
            runtimeStreamer == null
            &&
            (
                !hasExecutionState
                ||
                executionState !=
                    WorldGridViewportExecutionState.PlayMode
            )
        )
        {
            return;
        }

        ClearRuntimeStreamer();

        BoundsChanged?.Invoke();
    }
}
