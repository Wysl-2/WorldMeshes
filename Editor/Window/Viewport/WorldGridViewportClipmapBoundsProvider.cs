using System;
using UnityEditor;
using UnityEngine;

internal sealed class WorldGridViewportClipmapBoundsProvider
{
    private TerrainClipmapController runtimeController;

    private bool hasExecutionState;

    private WorldGridViewportExecutionState executionState;

    internal event Action BoundsChanged;

    internal WorldGridViewportClipmapBoundsProvider()
    {
        TerrainAuthoringSceneViewController
            .AppliedClipmapBoundsChanged +=
                HandleSourceBoundsChanged;

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
                    TerrainAuthoringSceneViewController
                        .TryGetAppliedClipmapBounds(
                            out minimumXZ,
                            out maximumXZ
                        );

            case WorldGridViewportExecutionState.PlayMode:
                if (!EnsureRuntimeController())
                {
                    return false;
                }

                return
                    runtimeController
                        .TryGetAppliedClipmapBounds(
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
            ClearRuntimeController();
        }
    }

    private bool EnsureRuntimeController()
    {
        if (runtimeController != null)
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

        TerrainClipmapController controller =
            clipmapRoot
                .GetComponent<TerrainClipmapController>();

        if (controller == null)
        {
            return false;
        }

        SetRuntimeController(
            controller
        );

        return true;
    }

    private void SetRuntimeController(
        TerrainClipmapController controller
    )
    {
        if (
            runtimeController ==
            controller
        )
        {
            return;
        }

        ClearRuntimeController();

        runtimeController =
            controller;

        if (runtimeController != null)
        {
            runtimeController
                .AppliedClipmapBoundsChanged +=
                    HandleSourceBoundsChanged;
        }
    }

    private void ClearRuntimeController()
    {
        if (runtimeController != null)
        {
            runtimeController
                .AppliedClipmapBoundsChanged -=
                    HandleSourceBoundsChanged;
        }

        runtimeController =
            null;
    }

    private void HandleSourceBoundsChanged()
    {
        BoundsChanged?.Invoke();
    }

    private void HandlePlayModeStateChanged(
        PlayModeStateChange state
    )
    {
        _ =
            state;

        hasExecutionState =
            false;

        ClearRuntimeController();
    }

    private void HandleHierarchyChanged()
    {
        if (
            runtimeController == null
            &&
            executionState !=
                WorldGridViewportExecutionState.PlayMode
        )
        {
            return;
        }

        ClearRuntimeController();

        BoundsChanged?.Invoke();
    }
}
