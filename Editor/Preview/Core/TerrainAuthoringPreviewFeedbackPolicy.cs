internal enum TerrainAuthoringPreviewFeedbackKind
{
    None, Paused, ResidencyFailure, ResidencyLoading, DirtyFailure, Updating
}

internal static class TerrainAuthoringPreviewFeedbackPolicy
{
    internal static bool ShouldShowLoading(bool waitingForCoverage, double waitingDurationSeconds, double delaySeconds) =>
        waitingForCoverage && waitingDurationSeconds >= delaySeconds;

    internal static bool ShouldShowFailure(bool hasFailure, bool failureAffectsRequiredCoverage) =>
        hasFailure && failureAffectsRequiredCoverage;

    internal static TerrainAuthoringPreviewFeedbackKind SelectFeedbackKind(bool enabled, bool controlling,
        bool workAllowed, bool waiting, bool requiredFailure, bool drawable, bool dirtyFailure, bool convergence)
    {
        if (!enabled || !controlling) return TerrainAuthoringPreviewFeedbackKind.None;
        if (!workAllowed) return waiting || convergence || dirtyFailure
            ? TerrainAuthoringPreviewFeedbackKind.Paused : TerrainAuthoringPreviewFeedbackKind.None;
        if (requiredFailure) return TerrainAuthoringPreviewFeedbackKind.ResidencyFailure;
        if (waiting) return TerrainAuthoringPreviewFeedbackKind.ResidencyLoading;
        if (!drawable) return TerrainAuthoringPreviewFeedbackKind.None;
        if (dirtyFailure) return TerrainAuthoringPreviewFeedbackKind.DirtyFailure;
        return convergence ? TerrainAuthoringPreviewFeedbackKind.Updating : TerrainAuthoringPreviewFeedbackKind.None;
    }

    internal static TerrainAuthoringPreviewFeedbackKind SelectFeedbackKind(TerrainAuthoringPreviewDiagnosticsSnapshot snapshot) =>
        SelectFeedbackKind(snapshot.Enabled, snapshot.HasControllingSceneView, snapshot.PreviewWorkAllowed,
            snapshot.WaitingForCoverage, ShouldShowFailure(snapshot.HasTransitionFailure, snapshot.FailureAffectsRequiredCoverage),
            snapshot.Drawable, snapshot.HasFailedDirtyUpdates, snapshot.AuthoringConvergencePending);

    internal static bool HasDisplayProgress(TerrainAuthoringPreviewWorkerSnapshot worker) =>
        worker.Present && worker.Purpose == TerrainAuthoringPreviewCachePublication.DisplayHeightSet;
}