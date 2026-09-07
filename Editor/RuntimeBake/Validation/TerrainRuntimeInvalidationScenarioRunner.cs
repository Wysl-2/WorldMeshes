using System;
using System.Collections.Generic;
using UnityEngine;

public static class TerrainRuntimeInvalidationScenarioRunner
{
    private static TerrainRuntimeInvalidationModifierBaseline modifierBaseline;
    private static TerrainRuntimeInvalidationSettingsBaseline settingsBaseline;

    private static readonly HashSet<Vector2Int>
        aggregatedHeightCoordinates =
            new HashSet<Vector2Int>();

    private static bool aggregationActive;
    private static int aggregationMutationCount;

    private static TerrainRuntimeInvalidationValidationResult lastResult;

    public static TerrainRuntimeInvalidationModifierBaseline ModifierBaseline =>
        modifierBaseline;

    public static TerrainRuntimeInvalidationSettingsBaseline SettingsBaseline =>
        settingsBaseline;

    public static bool HasModifierBaseline =>
        modifierBaseline != null;

    public static bool HasSettingsBaseline =>
        settingsBaseline != null;

    public static bool AggregationActive =>
        aggregationActive;

    public static int AggregationMutationCount =>
        aggregationMutationCount;

    public static int AggregatedHeightCoordinateCount =>
        aggregatedHeightCoordinates.Count;

    public static TerrainRuntimeInvalidationValidationResult LastResult =>
        lastResult;

    public static bool CaptureSelectedModifierBaseline(
        out string errorMessage
    )
    {
        TerrainRuntimeInvalidationModifierBaseline captured =
            TerrainRuntimeInvalidationValidationUtility
                .CaptureSelectedModifierBaseline(
                    out errorMessage
                );

        if (captured == null)
        {
            return false;
        }

        modifierBaseline = captured;

        aggregationActive = false;
        aggregationMutationCount = 0;
        aggregatedHeightCoordinates.Clear();

        return true;
    }

    public static bool CaptureSettingsBaseline(
        out string errorMessage
    )
    {
        TerrainRuntimeInvalidationSettingsBaseline captured =
            TerrainRuntimeInvalidationValidationUtility
                .CaptureSettingsBaseline(
                    out errorMessage
                );

        if (captured == null)
        {
            return false;
        }

        settingsBaseline = captured;
        return true;
    }

    public static TerrainRuntimeInvalidationValidationResult
        ValidateModifierMutation(
            TerrainRuntimeInvalidationScenario scenario
        )
    {
        lastResult =
            TerrainRuntimeInvalidationValidationUtility
                .ValidateModifierMutation(
                    scenario,
                    modifierBaseline
                );

        return lastResult;
    }

    public static TerrainRuntimeInvalidationValidationResult
        ValidateSettingsMutation(
            TerrainRuntimeInvalidationScenario scenario
        )
    {
        lastResult =
            TerrainRuntimeInvalidationValidationUtility
                .ValidateSettingsMutation(
                    scenario,
                    settingsBaseline
                );

        return lastResult;
    }

    public static bool BeginAggregation(
        out string errorMessage
    )
    {
        if (
            !CaptureSelectedModifierBaseline(
                out errorMessage
            )
        )
        {
            return false;
        }

        aggregationActive = true;
        aggregationMutationCount = 0;
        aggregatedHeightCoordinates.Clear();

        return true;
    }

    public static bool AccumulateCurrentModifierMutation(
        out string errorMessage
    )
    {
        errorMessage = "";

        if (!aggregationActive || modifierBaseline == null)
        {
            errorMessage =
                "Begin an aggregation baseline before recording modifier mutations.";
            return false;
        }

        if (
            !TerrainRuntimeInvalidationValidationUtility
                .TryCaptureCurrentModifier(
                    modifierBaseline,
                    out TerrainRuntimeInvalidationModifierBaseline current,
                    out errorMessage
                )
        )
        {
            return false;
        }

        if (
            !TerrainRuntimeInvalidationValidationUtility
                .TryLoadWorldSettings(
                    out WorldSettings worldSettings
                )
        )
        {
            errorMessage = "WorldSettings is unavailable.";
            return false;
        }

        if (
            modifierBaseline.Exists
            && current.Exists
            && modifierBaseline.ContentSignature ==
                current.ContentSignature
        )
        {
            errorMessage =
                "The current modifier state does not differ from the previous aggregation checkpoint.";
            return false;
        }

        if (
            !TerrainRuntimeInvalidationValidationUtility
                .TryCollectModifierTransitionHeightTiles(
                    modifierBaseline,
                    current,
                    worldSettings,
                    aggregatedHeightCoordinates,
                    out errorMessage
                )
        )
        {
            return false;
        }

        modifierBaseline = current;
        aggregationMutationCount++;

        return true;
    }

    public static TerrainRuntimeInvalidationValidationResult
        ValidateAggregation(
            TerrainRuntimeInvalidationScenario scenario
        )
    {
        if (
            scenario != TerrainRuntimeInvalidationScenario.MultipleEdits
            && scenario != TerrainRuntimeInvalidationScenario.RepeatedRegionEdits
            && scenario != TerrainRuntimeInvalidationScenario.DisconnectedRegionEdits
        )
        {
            lastResult =
                TerrainRuntimeInvalidationValidationUtility
                    .ValidateAggregatedModifierMutations(
                        TerrainRuntimeInvalidationScenario.MultipleEdits,
                        Array.Empty<Vector2Int>()
                    );

            return lastResult;
        }

        lastResult =
            TerrainRuntimeInvalidationValidationUtility
                .ValidateAggregatedModifierMutations(
                    scenario,
                    aggregatedHeightCoordinates
                );

        return lastResult;
    }

    public static bool BakeCurrentInvalidationAndValidate(
        Action<TerrainRuntimeInvalidationValidationResult> onCompleted = null
    )
    {
        if (lastResult == null || !lastResult.Passed)
        {
            return false;
        }

        if (
            TerrainRuntimeBakePipeline.IsRunning
            || TerrainRuntimeBakeValidationUtility.IsValidationBakeRunning
            || TerrainRuntimeBakeResumeValidationUtility.IsRunning
            || TerrainSurfaceMaskCompiler.IsGenerating
        )
        {
            return false;
        }

        return TerrainRuntimeBakeValidationUtility.BakePendingChanges(
            validation =>
            {
                if (lastResult != null)
                {
                    lastResult.AttachBakeValidation(
                        validation
                    );
                }

                try
                {
                    onCompleted?.Invoke(
                        lastResult
                    );
                }
                catch (Exception exception)
                {
                    Debug.LogException(
                        exception
                    );
                }
            }
        );
    }

    public static void ClearSession()
    {
        modifierBaseline = null;
        settingsBaseline = null;

        aggregationActive = false;
        aggregationMutationCount = 0;
        aggregatedHeightCoordinates.Clear();

        lastResult = null;
    }
}
