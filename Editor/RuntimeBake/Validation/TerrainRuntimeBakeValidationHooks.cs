using System;
using UnityEditor;

/*
 * Package 10.2 transient validation hooks.
 *
 * These hooks are editor-session only and are disabled by default. They exist
 * solely to create deterministic safe-boundary cancellation/failure scenarios.
 */
[InitializeOnLoad]
public static class TerrainRuntimeBakeValidationHooks
{
    private static TerrainRuntimeBakePipelineState coordinateCancellationStage =
        TerrainRuntimeBakePipelineState.Idle;

    private static float coordinateCancellationFraction = 0.25f;
    private static bool coordinateCancellationConsumed;

    private static TerrainRuntimeBakePipelineState failureBeforeStage =
        TerrainRuntimeBakePipelineState.Idle;

    private static bool failureBeforeStageConsumed;

    private static bool requestCancelDuringAddressables;
    private static bool requestCancelDuringAddressablesConsumed;

    private static bool requestCancelBeforeSceneSync;
    private static bool requestCancelBeforeSceneSyncConsumed;

    public static bool HasActiveHooks =>
        coordinateCancellationStage != TerrainRuntimeBakePipelineState.Idle
        || failureBeforeStage != TerrainRuntimeBakePipelineState.Idle
        || requestCancelDuringAddressables
        || requestCancelBeforeSceneSync;

    static TerrainRuntimeBakeValidationHooks()
    {
        ClearActiveHooks();
    }

    public static void ConfigureCoordinateCancellation(
        TerrainRuntimeBakePipelineState stage,
        float fraction = 0.25f
    )
    {
        ClearActiveHooks();

        if (
            stage != TerrainRuntimeBakePipelineState.Heightmaps
            && stage != TerrainRuntimeBakePipelineState.SurfaceMasks
            && stage != TerrainRuntimeBakePipelineState.Collision
        )
        {
            throw new ArgumentException(
                "Coordinate cancellation is supported only for Heightmaps, SurfaceMasks, and Collision.",
                nameof(stage)
            );
        }

        coordinateCancellationStage = stage;
        coordinateCancellationFraction = Math.Max(0.01f, Math.Min(0.99f, fraction));
        coordinateCancellationConsumed = false;
    }

    public static void ConfigureFailureBeforeStage(
        TerrainRuntimeBakePipelineState stage
    )
    {
        ClearActiveHooks();
        failureBeforeStage = stage;
        failureBeforeStageConsumed = false;
    }

    public static void ConfigureCancelDuringAddressables()
    {
        ClearActiveHooks();
        requestCancelDuringAddressables = true;
        requestCancelDuringAddressablesConsumed = false;
    }

    public static void ConfigureCancelBeforeSceneSync()
    {
        ClearActiveHooks();
        requestCancelBeforeSceneSync = true;
        requestCancelBeforeSceneSyncConsumed = false;
    }

    public static void ClearActiveHooks()
    {
        coordinateCancellationStage = TerrainRuntimeBakePipelineState.Idle;
        coordinateCancellationFraction = 0.25f;
        coordinateCancellationConsumed = false;

        failureBeforeStage = TerrainRuntimeBakePipelineState.Idle;
        failureBeforeStageConsumed = false;

        requestCancelDuringAddressables = false;
        requestCancelDuringAddressablesConsumed = false;

        requestCancelBeforeSceneSync = false;
        requestCancelBeforeSceneSyncConsumed = false;
    }

    internal static bool ShouldCancelCoordinateStage(
        TerrainRuntimeBakePipelineState stage,
        int succeededCount,
        int totalCount
    )
    {
        if (
            coordinateCancellationConsumed
            || coordinateCancellationStage != stage
            || totalCount < 2
            || succeededCount <= 0
            || succeededCount >= totalCount
        )
        {
            return false;
        }

        int threshold = Math.Max(
            1,
            (int)Math.Ceiling(totalCount * coordinateCancellationFraction)
        );

        if (succeededCount < threshold)
        {
            return false;
        }

        coordinateCancellationConsumed = true;
        return true;
    }

    internal static bool TryConsumeFailureBeforeStage(
        TerrainRuntimeBakePipelineState stage,
        out string message
    )
    {
        message = "";

        if (
            failureBeforeStageConsumed
            || failureBeforeStage != stage
        )
        {
            return false;
        }

        failureBeforeStageConsumed = true;
        message =
            "Package 10.2 validation intentionally injected a non-destructive failure before " +
            GetStageLabel(stage) + ".";
        return true;
    }

    internal static bool ShouldRequestCancelDuringAddressables()
    {
        if (
            !requestCancelDuringAddressables
            || requestCancelDuringAddressablesConsumed
        )
        {
            return false;
        }

        requestCancelDuringAddressablesConsumed = true;
        return true;
    }

    internal static bool ShouldRequestCancelBeforeSceneSync()
    {
        if (
            !requestCancelBeforeSceneSync
            || requestCancelBeforeSceneSyncConsumed
        )
        {
            return false;
        }

        requestCancelBeforeSceneSyncConsumed = true;
        return true;
    }

    private static string GetStageLabel(TerrainRuntimeBakePipelineState stage)
    {
        switch (stage)
        {
            case TerrainRuntimeBakePipelineState.Heightmaps:
                return "Runtime Heightmaps";
            case TerrainRuntimeBakePipelineState.SurfaceMasks:
                return "Surface Masks";
            case TerrainRuntimeBakePipelineState.Collision:
                return "Collision Meshes";
            case TerrainRuntimeBakePipelineState.Addressables:
                return "Addressables";
            case TerrainRuntimeBakePipelineState.SceneSync:
                return "Runtime Scene Synchronization";
            default:
                return stage.ToString();
        }
    }
}
