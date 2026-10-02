public static class TerrainAuthoringCompositionRegressionSuite
{
    public static TerrainValidationSuiteRunner CreateRunner()
    {
        return
            new TerrainValidationSuiteRunner(
                new[]
                {
                    new TerrainValidationSuiteRunner.Case(
                        "Modifier Data",
                        () =>
                        {
                            if (TerrainModifierDataValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainModifierDataValidationUtility.ValidateModifierDataFoundation();
                            return true;
                        },
                        () => TerrainModifierDataValidationUtility.IsRunning,
                        () => TerrainModifierDataValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Modifier Mutation Pipeline",
                        () =>
                        {
                            if (TerrainAuthoringModifierServiceValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainAuthoringModifierServiceValidationUtility.RequestValidation();
                            return TerrainAuthoringModifierServiceValidationUtility.IsRunning;
                        },
                        () => TerrainAuthoringModifierServiceValidationUtility.IsRunning,
                        () => TerrainAuthoringModifierServiceValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "GPU Height Compositor",
                        () =>
                        {
                            if (TerrainHeightCompositorValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainHeightCompositorValidationUtility.RequestValidation();
                            return TerrainHeightCompositorValidationUtility.IsRunning;
                        },
                        () => TerrainHeightCompositorValidationUtility.IsRunning,
                        () => TerrainHeightCompositorValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Target-Surface Blend",
                        () =>
                        {
                            if (
                                TerrainTargetSurfaceBlendValidationUtility.IsScheduled
                                || TerrainTargetSurfaceBlendValidationUtility.IsRunning
                            )
                            {
                                return false;
                            }

                            TerrainTargetSurfaceBlendValidationUtility.RequestValidation();
                            return
                                TerrainTargetSurfaceBlendValidationUtility.IsScheduled
                                || TerrainTargetSurfaceBlendValidationUtility.IsRunning;
                        },
                        () =>
                            TerrainTargetSurfaceBlendValidationUtility.IsScheduled
                            || TerrainTargetSurfaceBlendValidationUtility.IsRunning,
                        () => TerrainTargetSurfaceBlendValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Max / Min Blend",
                        () =>
                        {
                            if (
                                TerrainMaxMinBlendValidationUtility.IsScheduled
                                || TerrainMaxMinBlendValidationUtility.IsRunning
                            )
                            {
                                return false;
                            }

                            TerrainMaxMinBlendValidationUtility.RequestValidation();
                            return
                                TerrainMaxMinBlendValidationUtility.IsScheduled
                                || TerrainMaxMinBlendValidationUtility.IsRunning;
                        },
                        () =>
                            TerrainMaxMinBlendValidationUtility.IsScheduled
                            || TerrainMaxMinBlendValidationUtility.IsRunning,
                        () => TerrainMaxMinBlendValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Replace Blend",
                        () =>
                        {
                            if (
                                TerrainReplaceBlendValidationUtility.IsScheduled
                                || TerrainReplaceBlendValidationUtility.IsRunning
                            )
                            {
                                return false;
                            }

                            TerrainReplaceBlendValidationUtility.RequestValidation();
                            return
                                TerrainReplaceBlendValidationUtility.IsScheduled
                                || TerrainReplaceBlendValidationUtility.IsRunning;
                        },
                        () =>
                            TerrainReplaceBlendValidationUtility.IsScheduled
                            || TerrainReplaceBlendValidationUtility.IsRunning,
                        () => TerrainReplaceBlendValidationUtility.LastRunSummary
                    )
                }
            );
    }
}
