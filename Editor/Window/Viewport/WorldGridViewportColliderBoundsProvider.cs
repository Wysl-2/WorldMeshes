using System;
using UnityEditor;
using UnityEngine;

internal sealed class WorldGridViewportColliderBoundsProvider
{
    private TerrainCollisionColliderPool runtimeColliderPool;

    private bool hasExecutionState;

    private WorldGridViewportExecutionState executionState;

    internal event Action BoundsChanged;

    internal WorldGridViewportColliderBoundsProvider()
    {
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

        if (
            context.ExecutionState !=
                WorldGridViewportExecutionState.PlayMode
        )
        {
            return false;
        }

        if (!EnsureRuntimeColliderPool())
        {
            return false;
        }

        return
            runtimeColliderPool
                .TryGetActiveColliderWorldCoverage(
                    out minimumXZ,
                    out maximumXZ
                );
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
            ClearRuntimeColliderPool();
        }
    }

    private bool EnsureRuntimeColliderPool()
    {
        if (runtimeColliderPool != null)
        {
            return true;
        }

        if (
            !TerrainWorldSceneUtility
                .TryFindActiveCollisionRoot(
                    out Transform collisionRoot,
                    out _
                )
            ||
            collisionRoot == null
        )
        {
            return false;
        }

        TerrainCollisionColliderPool colliderPool =
            collisionRoot
                .GetComponent<TerrainCollisionColliderPool>();

        if (colliderPool == null)
        {
            return false;
        }

        SetRuntimeColliderPool(
            colliderPool
        );

        return true;
    }

    private void SetRuntimeColliderPool(
        TerrainCollisionColliderPool colliderPool
    )
    {
        if (
            runtimeColliderPool ==
            colliderPool
        )
        {
            return;
        }

        ClearRuntimeColliderPool();

        runtimeColliderPool =
            colliderPool;

        if (runtimeColliderPool != null)
        {
            runtimeColliderPool
                .ActiveColliderCoverageChanged +=
                    HandleRuntimeCoverageChanged;
        }
    }

    private void ClearRuntimeColliderPool()
    {
        if (runtimeColliderPool != null)
        {
            runtimeColliderPool
                .ActiveColliderCoverageChanged -=
                    HandleRuntimeCoverageChanged;
        }

        runtimeColliderPool =
            null;
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

        ClearRuntimeColliderPool();
    }

    private void HandleHierarchyChanged()
    {
        if (
            runtimeColliderPool == null
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

        ClearRuntimeColliderPool();

        BoundsChanged?.Invoke();
    }
}
