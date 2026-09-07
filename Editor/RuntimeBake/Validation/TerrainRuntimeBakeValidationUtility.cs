using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/*
 * Package 10.1 validation facade.
 *
 * Validation deliberately runs the Package 08 unified pipeline rather than
 * calling individual generators. The planner and lower-level stage results
 * remain the authoritative sources for expected and actual work.
 */
public static class TerrainRuntimeBakeValidationUtility
{
    private static bool validationBakeRunning;
    private static TerrainRuntimeBakeValidationResult lastResult;

    public static bool IsValidationBakeRunning => validationBakeRunning;
    public static TerrainRuntimeBakeValidationResult LastResult => lastResult;

    public static bool BakePendingChanges(
        Action<TerrainRuntimeBakeValidationResult> onCompleted = null
    )
    {
        if (validationBakeRunning)
        {
            Debug.LogWarning(
                "A WorldMeshes runtime pipeline validation bake is already running."
            );

            return false;
        }

        TerrainRuntimeBakePlan capturedInitialPlan =
            TerrainRuntimeBakePlanner.BuildCurrentPlan();

        validationBakeRunning = true;

        bool started = TerrainRuntimeBakePipeline.BakePendingChanges(
            pipelineResult =>
            {
                validationBakeRunning = false;

                TerrainRuntimeBakeValidationResult validationResult =
                    BuildValidationResult(
                        pipelineResult,
                        capturedInitialPlan
                    );

                lastResult = validationResult;

                try
                {
                    onCompleted?.Invoke(validationResult);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        );

        /*
         * Package 08 can invoke a blocked/rejected completion callback
         * synchronously. Only clear the flag here when no callback consumed it.
         */
        if (!started && validationBakeRunning)
        {
            validationBakeRunning = false;

            TerrainRuntimeBakePipelineResult pipelineResult =
                TerrainRuntimeBakePipeline.LastResult;

            lastResult = BuildValidationResult(
                pipelineResult,
                capturedInitialPlan
            );

            try
            {
                onCompleted?.Invoke(lastResult);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        return started;
    }

    public static TerrainRuntimeBakeValidationResult ValidatePipelineResult(
        TerrainRuntimeBakePipelineResult pipelineResult
    )
    {
        TerrainRuntimeBakeValidationResult result =
            BuildValidationResult(
                pipelineResult,
                pipelineResult != null
                    ? pipelineResult.InitialPlan
                    : null
            );

        lastResult = result;
        return result;
    }

    private static TerrainRuntimeBakeValidationResult BuildValidationResult(
        TerrainRuntimeBakePipelineResult pipelineResult,
        TerrainRuntimeBakePlan capturedInitialPlan
    )
    {
        if (pipelineResult == null)
        {
            return new TerrainRuntimeBakeValidationResult(
                TerrainRuntimeBakeValidationOutcome.Failed,
                null,
                capturedInitialPlan,
                null,
                null,
                null,
                null,
                null,
                false,
                null,
                null,
                "The unified runtime bake pipeline produced no result.",
                "Runtime pipeline validation could not run."
            );
        }

        WorldSettings worldSettings =
            AssetDatabase.LoadAssetAtPath<WorldSettings>(
                WorldMeshesPaths.WorldSettingsAssetPath
            );

        List<string> warnings = new List<string>();

        if (worldSettings == null)
        {
            warnings.Add(
                "WorldSettings is unavailable, so complete Full-stage coordinate expectations could not be derived."
            );
        }

        TerrainRuntimeBakeStageValidationResult heightValidation =
            BuildHeightValidation(
                pipelineResult,
                worldSettings
            );

        TerrainRuntimeBakeStageValidationResult surfaceValidation =
            BuildSurfaceValidation(
                pipelineResult,
                worldSettings
            );

        TerrainRuntimeBakeStageValidationResult collisionValidation =
            BuildCollisionValidation(
                pipelineResult,
                worldSettings
            );

        TerrainRuntimeAddressablesStageValidationResult addressablesValidation =
            new TerrainRuntimeAddressablesStageValidationResult(
                pipelineResult.AddressablesPlan,
                pipelineResult.AddressablesStageExecuted,
                pipelineResult.AddressablesResult
            );

        TerrainRuntimeSceneStageValidationResult sceneValidation =
            new TerrainRuntimeSceneStageValidationResult(
                pipelineResult.Mode,
                pipelineResult.SceneSyncPlan,
                pipelineResult.SceneSyncStageExecuted,
                pipelineResult.SceneSyncResult
            );

        bool finalPlanCurrent =
            pipelineResult.FinalPlan != null
            && !pipelineResult.FinalPlan.IsBlocked
            && !pipelineResult.FinalPlan.HasWork;

        bool stagePlansRequired =
            pipelineResult.Mode == TerrainRuntimeBakePipelineMode.RebuildAll
            || (
                pipelineResult.InitialPlan != null
                && pipelineResult.InitialPlan.HasWork
            );

        bool stagePlansCaptured =
            !stagePlansRequired
            || (
                pipelineResult.HeightPlan != null
                && pipelineResult.SurfacePlan != null
                && pipelineResult.CollisionPlan != null
                && pipelineResult.AddressablesPlan != null
                && pipelineResult.SceneSyncPlan != null
            );

        if (!stagePlansCaptured)
        {
            warnings.Add(
                "One or more Package 08 stage-local plans were not captured for a run that reached the runtime bake stages."
            );
        }

        bool comparisonsPassed =
            stagePlansCaptured
            && IsStageAcceptable(heightValidation)
            && IsStageAcceptable(surfaceValidation)
            && IsStageAcceptable(collisionValidation)
            && IsAddressablesAcceptable(addressablesValidation)
            && IsSceneAcceptable(sceneValidation);

        TerrainRuntimeBakeValidationOutcome outcome;
        string errorMessage = "";
        string summaryMessage;

        switch (pipelineResult.Outcome)
        {
            case TerrainRuntimeBakePipelineOutcome.Blocked:
                outcome = TerrainRuntimeBakeValidationOutcome.Blocked;
                errorMessage = pipelineResult.ErrorMessage;
                summaryMessage =
                    "The canonical runtime pipeline was blocked. Captured stage comparisons remain available for diagnosis.";
                break;

            case TerrainRuntimeBakePipelineOutcome.Cancelled:
                outcome = TerrainRuntimeBakeValidationOutcome.Cancelled;
                summaryMessage =
                    "The canonical runtime pipeline was cancelled. Partial result partition and plan/request consistency were preserved for diagnosis.";
                break;

            case TerrainRuntimeBakePipelineOutcome.Failed:
                outcome = TerrainRuntimeBakeValidationOutcome.Failed;
                errorMessage = pipelineResult.ErrorMessage;
                summaryMessage =
                    "The canonical runtime pipeline failed. Earlier successful work was not rolled back by validation.";
                break;

            case TerrainRuntimeBakePipelineOutcome.CompletedWithWarnings:
                if (!comparisonsPassed || !finalPlanCurrent)
                {
                    outcome = TerrainRuntimeBakeValidationOutcome.Failed;
                    errorMessage =
                        "The runtime pipeline completed, but Package 10.1 detected a planner/result validation mismatch.";
                    summaryMessage =
                        "Runtime generation completed, but validation did not certify the observed stage work.";
                }
                else
                {
                    outcome = TerrainRuntimeBakeValidationOutcome.PassedWithWarnings;
                    summaryMessage =
                        "Runtime changes were baked and validated successfully with pipeline warnings.";
                }
                break;

            case TerrainRuntimeBakePipelineOutcome.NoWork:
            case TerrainRuntimeBakePipelineOutcome.Completed:
                if (!comparisonsPassed || !finalPlanCurrent)
                {
                    outcome = TerrainRuntimeBakeValidationOutcome.Failed;
                    errorMessage =
                        "The runtime pipeline completed, but Package 10.1 detected a planner/result validation mismatch.";
                    summaryMessage =
                        "Runtime generation completed, but validation did not certify the observed stage work.";
                }
                else
                {
                    outcome = TerrainRuntimeBakeValidationOutcome.Passed;
                    summaryMessage =
                        pipelineResult.Outcome == TerrainRuntimeBakePipelineOutcome.NoWork
                            ? "No runtime work was pending and the current runtime state validated successfully."
                            : "Runtime changes were baked and the observed stage work matched the captured plans.";
                }
                break;

            default:
                outcome = TerrainRuntimeBakeValidationOutcome.Failed;
                errorMessage = "Unexpected unified runtime pipeline outcome.";
                summaryMessage = "Runtime pipeline validation did not complete.";
                break;
        }

        return new TerrainRuntimeBakeValidationResult(
            outcome,
            pipelineResult,
            capturedInitialPlan ?? pipelineResult.InitialPlan,
            heightValidation,
            surfaceValidation,
            collisionValidation,
            addressablesValidation,
            sceneValidation,
            finalPlanCurrent,
            null,
            warnings,
            errorMessage,
            summaryMessage
        );
    }

    private static TerrainRuntimeBakeStageValidationResult BuildHeightValidation(
        TerrainRuntimeBakePipelineResult pipelineResult,
        WorldSettings worldSettings
    )
    {
        TerrainRuntimeBakePlan plan = pipelineResult.HeightPlan;
        TerrainRuntimeHeightCompileResult result = pipelineResult.HeightResult;

        bool evaluated = plan != null;
        TerrainRuntimeBakeWorkMode expectedMode = GetExpectedCoordinateWorkMode(
            pipelineResult.Mode,
            plan != null ? plan.HeightWorkMode : TerrainRuntimeBakeWorkMode.None
        );

        List<Vector2Int> expected = GetExpectedHeightCoordinates(
            pipelineResult.Mode,
            plan,
            worldSettings
        );

        bool completed =
            result != null
            && (
                result.Outcome == TerrainRuntimeHeightCompileOutcome.Completed
                || result.Outcome == TerrainRuntimeHeightCompileOutcome.NoWork
            );

        return new TerrainRuntimeBakeStageValidationResult(
            "Heightmaps",
            evaluated,
            pipelineResult.HeightStageExecuted,
            completed,
            expectedMode,
            result != null ? result.WorkMode : TerrainRuntimeBakeWorkMode.None,
            expected,
            result != null ? result.RequestedTiles : null,
            result != null ? result.SucceededTiles : null,
            result != null ? result.FailedTiles : null,
            result != null ? result.UnprocessedTiles : null,
            result != null ? result.CreatedTileCount : 0,
            result != null ? result.UpdatedTileCount : 0,
            result != null ? result.RemovedTileCount : 0,
            result != null ? result.ErrorMessage : ""
        );
    }

    private static TerrainRuntimeBakeStageValidationResult BuildSurfaceValidation(
        TerrainRuntimeBakePipelineResult pipelineResult,
        WorldSettings worldSettings
    )
    {
        TerrainRuntimeBakePlan plan = pipelineResult.SurfacePlan;
        TerrainSurfaceMaskGenerationResult result = pipelineResult.SurfaceResult;

        bool evaluated = plan != null;
        TerrainRuntimeBakeWorkMode expectedMode = GetExpectedCoordinateWorkMode(
            pipelineResult.Mode,
            plan != null ? plan.SurfaceWorkMode : TerrainRuntimeBakeWorkMode.None
        );

        List<Vector2Int> expected = GetExpectedSurfaceCoordinates(
            pipelineResult.Mode,
            plan,
            worldSettings
        );

        bool completed =
            result != null
            && (
                result.Outcome == TerrainSurfaceMaskGenerationOutcome.Completed
                || result.Outcome == TerrainSurfaceMaskGenerationOutcome.NoWork
            );

        return new TerrainRuntimeBakeStageValidationResult(
            "Surface Masks",
            evaluated,
            pipelineResult.SurfaceStageExecuted,
            completed,
            expectedMode,
            result != null ? result.WorkMode : TerrainRuntimeBakeWorkMode.None,
            expected,
            result != null ? result.RequestedTiles : null,
            result != null ? result.SucceededTiles : null,
            result != null ? result.FailedTiles : null,
            result != null ? result.UnprocessedTiles : null,
            result != null ? result.CreatedTileCount : 0,
            result != null ? result.UpdatedTileCount : 0,
            result != null ? result.RemovedTileCount : 0,
            result != null ? result.ErrorMessage : ""
        );
    }

    private static TerrainRuntimeBakeStageValidationResult BuildCollisionValidation(
        TerrainRuntimeBakePipelineResult pipelineResult,
        WorldSettings worldSettings
    )
    {
        TerrainRuntimeBakePlan plan = pipelineResult.CollisionPlan;
        TerrainCollisionGenerationResult result = pipelineResult.CollisionResult;

        bool evaluated = plan != null;
        TerrainRuntimeBakeWorkMode expectedMode = GetExpectedCoordinateWorkMode(
            pipelineResult.Mode,
            plan != null ? plan.CollisionWorkMode : TerrainRuntimeBakeWorkMode.None
        );

        List<Vector2Int> expected = GetExpectedCollisionCoordinates(
            pipelineResult.Mode,
            plan,
            worldSettings
        );

        bool completed =
            result != null
            && (
                result.Outcome == TerrainCollisionGenerationOutcome.Completed
                || result.Outcome == TerrainCollisionGenerationOutcome.NoWork
            );

        return new TerrainRuntimeBakeStageValidationResult(
            "Collision",
            evaluated,
            pipelineResult.CollisionStageExecuted,
            completed,
            expectedMode,
            result != null ? result.WorkMode : TerrainRuntimeBakeWorkMode.None,
            expected,
            result != null ? result.RequestedChunks : null,
            result != null ? result.SucceededChunks : null,
            result != null ? result.FailedChunks : null,
            result != null ? result.UnprocessedChunks : null,
            result != null ? result.CreatedMeshCount : 0,
            result != null ? result.UpdatedMeshCount : 0,
            result != null ? result.RemovedMeshCount : 0,
            result != null ? result.ErrorMessage : ""
        );
    }

    private static TerrainRuntimeBakeWorkMode GetExpectedCoordinateWorkMode(
        TerrainRuntimeBakePipelineMode pipelineMode,
        TerrainRuntimeBakeWorkMode plannedMode
    )
    {
        return pipelineMode == TerrainRuntimeBakePipelineMode.RebuildAll
            ? TerrainRuntimeBakeWorkMode.Full
            : plannedMode;
    }

    private static List<Vector2Int> GetExpectedHeightCoordinates(
        TerrainRuntimeBakePipelineMode pipelineMode,
        TerrainRuntimeBakePlan plan,
        WorldSettings worldSettings
    )
    {
        if (plan == null)
        {
            return new List<Vector2Int>();
        }

        if (
            pipelineMode == TerrainRuntimeBakePipelineMode.RebuildAll
            || plan.HeightWorkMode == TerrainRuntimeBakeWorkMode.Full
        )
        {
            return CollectAllHeightTiles(worldSettings);
        }

        return CopySorted(plan.HeightTiles);
    }

    private static List<Vector2Int> GetExpectedSurfaceCoordinates(
        TerrainRuntimeBakePipelineMode pipelineMode,
        TerrainRuntimeBakePlan plan,
        WorldSettings worldSettings
    )
    {
        if (plan == null)
        {
            return new List<Vector2Int>();
        }

        if (
            pipelineMode == TerrainRuntimeBakePipelineMode.RebuildAll
            || plan.SurfaceWorkMode == TerrainRuntimeBakeWorkMode.Full
        )
        {
            /* Runtime Surface currently shares the complete Height tile grid. */
            return CollectAllHeightTiles(worldSettings);
        }

        return CopySorted(plan.SurfaceTiles);
    }

    private static List<Vector2Int> GetExpectedCollisionCoordinates(
        TerrainRuntimeBakePipelineMode pipelineMode,
        TerrainRuntimeBakePlan plan,
        WorldSettings worldSettings
    )
    {
        if (plan == null)
        {
            return new List<Vector2Int>();
        }

        if (
            pipelineMode == TerrainRuntimeBakePipelineMode.RebuildAll
            || plan.CollisionWorkMode == TerrainRuntimeBakeWorkMode.Full
        )
        {
            HashSet<Vector2Int> all = new HashSet<Vector2Int>();
            TerrainRuntimeBakeDependencyUtility.CollectAllCollisionChunks(
                worldSettings,
                all
            );
            return CopySorted(all);
        }

        return CopySorted(plan.CollisionChunks);
    }

    private static List<Vector2Int> CollectAllHeightTiles(
        WorldSettings worldSettings
    )
    {
        HashSet<Vector2Int> all = new HashSet<Vector2Int>();
        TerrainRuntimeBakeDependencyUtility.CollectAllHeightTiles(
            worldSettings,
            all
        );
        return CopySorted(all);
    }

    private static List<Vector2Int> CopySorted(
        IEnumerable<Vector2Int> source
    )
    {
        List<Vector2Int> result =
            new List<Vector2Int>(
                source != null
                    ? new HashSet<Vector2Int>(source)
                    : new HashSet<Vector2Int>()
            );

        result.Sort(TerrainRuntimeBakeStageValidationResult.CompareCoordinates);
        return result;
    }

    private static bool IsStageAcceptable(
        TerrainRuntimeBakeStageValidationResult result
    )
    {
        return result == null || !result.Evaluated || result.Passed;
    }

    private static bool IsAddressablesAcceptable(
        TerrainRuntimeAddressablesStageValidationResult result
    )
    {
        return result == null || !result.Evaluated || result.Passed;
    }

    private static bool IsSceneAcceptable(
        TerrainRuntimeSceneStageValidationResult result
    )
    {
        return result == null || !result.Evaluated || result.Passed;
    }
}
