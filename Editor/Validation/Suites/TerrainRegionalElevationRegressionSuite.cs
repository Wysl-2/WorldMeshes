public static class TerrainRegionalElevationRegressionSuite
{
    public static TerrainValidationSuiteRunner CreateRunner()
    {
        return
            new TerrainValidationSuiteRunner(
                new[]
                {
                    new TerrainValidationSuiteRunner.Case(
                        "Regional Elevation Data",
                        () =>
                        {
                            if (TerrainRegionalElevationDataValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationDataValidationUtility.ValidateRegionalElevationData();
                            return true;
                        },
                        () => TerrainRegionalElevationDataValidationUtility.IsRunning,
                        () => TerrainRegionalElevationDataValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Node Initialization",
                        () =>
                        {
                            if (TerrainNodeElevationInitializationValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainNodeElevationInitializationValidationUtility.ValidateNodeElevationInitialization();
                            return true;
                        },
                        () => TerrainNodeElevationInitializationValidationUtility.IsRunning,
                        () => TerrainNodeElevationInitializationValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Node Interpolation",
                        () =>
                        {
                            if (TerrainNodeElevationInterpolationValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainNodeElevationInterpolationValidationUtility.ValidateNodeElevationInterpolation();
                            return true;
                        },
                        () => TerrainNodeElevationInterpolationValidationUtility.IsRunning,
                        () => TerrainNodeElevationInterpolationValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Interpolation Modes",
                        () =>
                        {
                            if (TerrainRegionalElevationInterpolationModeValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationInterpolationModeValidationUtility.ValidateInterpolationModeFoundation();
                            return TerrainRegionalElevationInterpolationModeValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationInterpolationModeValidationUtility.IsRunning,
                        () => TerrainRegionalElevationInterpolationModeValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Triangulation",
                        () =>
                        {
                            if (TerrainRegionalElevationTriangulationValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationTriangulationValidationUtility.ValidateRegionalElevationTriangulation();
                            return TerrainRegionalElevationTriangulationValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationTriangulationValidationUtility.IsRunning,
                        () => TerrainRegionalElevationTriangulationValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Triangulated Linear CPU",
                        () =>
                        {
                            if (TerrainRegionalElevationTriangulatedLinearValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationTriangulatedLinearValidationUtility.ValidateTriangulatedLinearCpu();
                            return TerrainRegionalElevationTriangulatedLinearValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationTriangulatedLinearValidationUtility.IsRunning,
                        () => TerrainRegionalElevationTriangulatedLinearValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Triangulated Linear GPU",
                        () =>
                        {
                            if (TerrainRegionalElevationTriangulatedLinearGpuValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationTriangulatedLinearGpuValidationUtility.ValidateTriangulatedLinearGpu();
                            return TerrainRegionalElevationTriangulatedLinearGpuValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationTriangulatedLinearGpuValidationUtility.IsRunning,
                        () => TerrainRegionalElevationTriangulatedLinearGpuValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Smooth Gradients",
                        () =>
                        {
                            if (TerrainRegionalElevationSmoothGradientValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationSmoothGradientValidationUtility.ValidateSmoothGradients();
                            return TerrainRegionalElevationSmoothGradientValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationSmoothGradientValidationUtility.IsRunning,
                        () => TerrainRegionalElevationSmoothGradientValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Triangulated Smooth CPU",
                        () =>
                        {
                            if (TerrainRegionalElevationTriangulatedSmoothCpuValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationTriangulatedSmoothCpuValidationUtility.ValidateTriangulatedSmoothCpu();
                            return TerrainRegionalElevationTriangulatedSmoothCpuValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationTriangulatedSmoothCpuValidationUtility.IsRunning,
                        () => TerrainRegionalElevationTriangulatedSmoothCpuValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Triangulated Smooth GPU",
                        () =>
                        {
                            if (TerrainRegionalElevationTriangulatedSmoothGpuValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationTriangulatedSmoothGpuValidationUtility.ValidateTriangulatedSmoothGpu();
                            return TerrainRegionalElevationTriangulatedSmoothGpuValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationTriangulatedSmoothGpuValidationUtility.IsRunning,
                        () => TerrainRegionalElevationTriangulatedSmoothGpuValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Regional Composition",
                        () =>
                        {
                            if (TerrainRegionalElevationCompositionValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationCompositionValidationUtility.ValidateRegionalElevationComposition();
                            return TerrainRegionalElevationCompositionValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationCompositionValidationUtility.IsRunning,
                        () => TerrainRegionalElevationCompositionValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Node Management",
                        () =>
                        {
                            if (TerrainRegionalElevationManagementValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationManagementValidationUtility.ValidateRegionalElevationManagement();
                            return TerrainRegionalElevationManagementValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationManagementValidationUtility.IsRunning,
                        () => TerrainRegionalElevationManagementValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Multi-Selection",
                        () =>
                        {
                            if (TerrainRegionalElevationMultiSelectValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationMultiSelectValidationUtility.ValidateRegionalElevationMultiSelect();
                            return TerrainRegionalElevationMultiSelectValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationMultiSelectValidationUtility.IsRunning,
                        () => TerrainRegionalElevationMultiSelectValidationUtility.LastRunSummary
                    ),
                    new TerrainValidationSuiteRunner.Case(
                        "Scene Tool",
                        () =>
                        {
                            if (TerrainRegionalElevationSceneToolValidationUtility.IsRunning)
                            {
                                return false;
                            }

                            TerrainRegionalElevationSceneToolValidationUtility.ValidateRegionalElevationSceneTool();
                            return TerrainRegionalElevationSceneToolValidationUtility.IsRunning;
                        },
                        () => TerrainRegionalElevationSceneToolValidationUtility.IsRunning,
                        () => TerrainRegionalElevationSceneToolValidationUtility.LastRunSummary
                    )
                }
            );
    }
}
