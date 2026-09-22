internal static class TerrainAuthoringPreviewFeedbackPolicy
{
    internal static bool ShouldShowLoading(
        bool waitingForCoverage,
        double waitingDurationSeconds,
        double delaySeconds
    )
    {
        return
            waitingForCoverage
            &&
            waitingDurationSeconds >=
                delaySeconds;
    }

    internal static bool ShouldShowFailure(
        bool hasFailure,
        bool failureAffectsRequiredCoverage
    )
    {
        return
            hasFailure
            &&
            failureAffectsRequiredCoverage;
    }
}
