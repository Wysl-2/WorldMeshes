public static class TerrainStampRegressionSuite
{
    public static TerrainValidationSuiteRunner CreateRunner()
    {
        return
            new TerrainValidationSuiteRunner(
                new[]
                {
                    CreateCase(
                        "Stamp Asset Defaults",
                        () => TerrainHeightStampAssetDefaultsValidationUtility.IsScheduled,
                        () => TerrainHeightStampAssetDefaultsValidationUtility.IsRunning,
                        TerrainHeightStampAssetDefaultsValidationUtility.RequestValidation,
                        () => TerrainHeightStampAssetDefaultsValidationUtility.LastRunSummary
                    ),
                    CreateCase(
                        "Stamp Library",
                        () => TerrainHeightStampLibraryValidationUtility.IsScheduled,
                        () => TerrainHeightStampLibraryValidationUtility.IsRunning,
                        TerrainHeightStampLibraryValidationUtility.RequestValidation,
                        () => TerrainHeightStampLibraryValidationUtility.LastRunSummary
                    ),
                    CreateCase(
                        "Stamp Library Synchronization",
                        () => TerrainHeightStampLibrarySyncValidationUtility.IsScheduled,
                        () => TerrainHeightStampLibrarySyncValidationUtility.IsRunning,
                        TerrainHeightStampLibrarySyncValidationUtility.RequestValidation,
                        () => TerrainHeightStampLibrarySyncValidationUtility.LastRunSummary
                    ),
                    CreateCase(
                        "Stamp Library Browser",
                        () => TerrainHeightStampLibraryBrowserValidationUtility.IsScheduled,
                        () => TerrainHeightStampLibraryBrowserValidationUtility.IsRunning,
                        TerrainHeightStampLibraryBrowserValidationUtility.RequestValidation,
                        () => TerrainHeightStampLibraryBrowserValidationUtility.LastRunSummary
                    ),
                    CreateCase(
                        "Stamp Rotation",
                        () => TerrainStampRotationValidationUtility.IsScheduled,
                        () => TerrainStampRotationValidationUtility.IsRunning,
                        TerrainStampRotationValidationUtility.RequestValidation,
                        () => TerrainStampRotationValidationUtility.LastRunSummary
                    ),
                    CreateCase(
                        "Source Orientation",
                        () => TerrainStampSourceOrientationValidationUtility.IsScheduled,
                        () => TerrainStampSourceOrientationValidationUtility.IsRunning,
                        TerrainStampSourceOrientationValidationUtility.RequestValidation,
                        () => TerrainStampSourceOrientationValidationUtility.LastRunSummary
                    ),
                    CreateCase(
                        "Source Remapping",
                        () => TerrainStampSourceRemapValidationUtility.IsScheduled,
                        () => TerrainStampSourceRemapValidationUtility.IsRunning,
                        TerrainStampSourceRemapValidationUtility.RequestValidation,
                        () => TerrainStampSourceRemapValidationUtility.LastRunSummary
                    ),
                    CreateCase(
                        "Stamp Falloff",
                        () => TerrainStampFalloffValidationUtility.IsScheduled,
                        () => TerrainStampFalloffValidationUtility.IsRunning,
                        TerrainStampFalloffValidationUtility.RequestValidation,
                        () => TerrainStampFalloffValidationUtility.LastRunSummary
                    ),
                    CreateCase(
                        "Stamp Authoring Defaults",
                        () => TerrainStampAuthoringDefaultsValidationUtility.IsScheduled,
                        () => TerrainStampAuthoringDefaultsValidationUtility.IsRunning,
                        TerrainStampAuthoringDefaultsValidationUtility.RequestValidation,
                        () => TerrainStampAuthoringDefaultsValidationUtility.LastRunSummary
                    )
                }
            );
    }

    private static TerrainValidationSuiteRunner.Case CreateCase(
        string name,
        System.Func<bool> isScheduled,
        System.Func<bool> isRunning,
        System.Action requestValidation,
        System.Func<TerrainValidationRunSummary> getSummary
    )
    {
        return
            new TerrainValidationSuiteRunner.Case(
                name,
                () =>
                {
                    if (isScheduled() || isRunning())
                    {
                        return false;
                    }

                    requestValidation();

                    return
                        isScheduled()
                        || isRunning();
                },
                () =>
                    isScheduled()
                    || isRunning(),
                getSummary
            );
    }
}
