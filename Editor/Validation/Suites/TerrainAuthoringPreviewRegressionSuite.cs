public static class TerrainAuthoringPreviewRegressionSuite
{
    public static TerrainValidationSuiteRunner CreateRunner()
    {
        return
            new TerrainValidationSuiteRunner(
                new[]
                {
                    new TerrainValidationSuiteRunner.Case(
                        "Height Cache Window",
                        () =>
                        {
                            if (TerrainAuthoringPreviewCacheValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainAuthoringPreviewCacheValidationUtility.RequestValidation();
                            return TerrainAuthoringPreviewCacheValidationUtility.IsRunning;
                        },
                        () => TerrainAuthoringPreviewCacheValidationUtility.IsRunning,
                        () => TerrainAuthoringPreviewCacheValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Scene View Residency",
                        () =>
                        {
                            if (TerrainAuthoringSceneViewResidencyValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainAuthoringSceneViewResidencyValidationUtility.RequestValidation();
                            return TerrainAuthoringSceneViewResidencyValidationUtility.IsRunning;
                        },
                        () => TerrainAuthoringSceneViewResidencyValidationUtility.IsRunning,
                        () => TerrainAuthoringSceneViewResidencyValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Staged Window Transitions",
                        () =>
                        {
                            if (TerrainAuthoringStagedTransitionValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainAuthoringStagedTransitionValidationUtility.RequestValidation();
                            return TerrainAuthoringStagedTransitionValidationUtility.IsRunning;
                        },
                        () => TerrainAuthoringStagedTransitionValidationUtility.IsRunning,
                        () => TerrainAuthoringStagedTransitionValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Residency Size Recovery",
                        () =>
                        {
                            if (TerrainAuthoringResidencySizeRecoveryValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainAuthoringResidencySizeRecoveryValidationUtility.RequestValidation();
                            return TerrainAuthoringResidencySizeRecoveryValidationUtility.IsRunning;
                        },
                        () => TerrainAuthoringResidencySizeRecoveryValidationUtility.IsRunning,
                        () => TerrainAuthoringResidencySizeRecoveryValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Incremental Streaming",
                        () =>
                        {
                            if (TerrainAuthoringIncrementalStreamingValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainAuthoringIncrementalStreamingValidationUtility.RequestValidation();
                            return TerrainAuthoringIncrementalStreamingValidationUtility.IsRunning;
                        },
                        () => TerrainAuthoringIncrementalStreamingValidationUtility.IsRunning,
                        () => TerrainAuthoringIncrementalStreamingValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Modifier Residency",
                        () =>
                        {
                            if (TerrainAuthoringModifierResidencyValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainAuthoringModifierResidencyValidationUtility.RequestValidation();
                            return TerrainAuthoringModifierResidencyValidationUtility.IsRunning;
                        },
                        () => TerrainAuthoringModifierResidencyValidationUtility.IsRunning,
                        () => TerrainAuthoringModifierResidencyValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Regional Elevation Residency",
                        () =>
                        {
                            if (TerrainAuthoringRegionalElevationResidencyValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainAuthoringRegionalElevationResidencyValidationUtility.ValidateRegionalElevationResidency();
                            return TerrainAuthoringRegionalElevationResidencyValidationUtility.IsRunning;
                        },
                        () => TerrainAuthoringRegionalElevationResidencyValidationUtility.IsRunning,
                        () => TerrainAuthoringRegionalElevationResidencyValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Analysis Window Safety",
                        () =>
                        {
                            TerrainAuthoringAnalysisDecouplingValidationUtility.ValidateAnalysisDecoupling();
                            return true;
                        },
                        () => false,
                        () => TerrainAuthoringAnalysisDecouplingValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Preview Responsiveness",
                        () =>
                        {
                            if (TerrainAuthoringPreviewValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainAuthoringPreviewValidationUtility.ValidatePreviewResponsiveness();
                            return TerrainAuthoringPreviewValidationUtility.IsRunning;
                        },
                        () => TerrainAuthoringPreviewValidationUtility.IsRunning,
                        () => TerrainAuthoringPreviewValidationUtility.LastRunSummary
                    )

                }
            );
    }
}
