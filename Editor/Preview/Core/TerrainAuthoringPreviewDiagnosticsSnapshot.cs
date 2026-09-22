internal readonly struct TerrainAuthoringPreviewDiagnosticsSnapshot
{
    public readonly bool Enabled;
    public readonly bool CacheReady;

    public readonly TerrainAuthoringPreviewStatus PreviewStatus;
    public readonly string PreviewStatusMessage;

    public readonly bool HasActiveWindow;
    public readonly TerrainHeightCacheWindow ActiveWindow;

    public readonly bool HasRequiredWindow;
    public readonly TerrainHeightCacheWindow RequiredWindow;

    public readonly bool HasDesiredWindow;
    public readonly TerrainHeightCacheWindow DesiredWindow;

    public readonly bool HasRequestedWindow;
    public readonly TerrainHeightCacheWindow RequestedWindow;

    public readonly bool HasStagingWindow;
    public readonly TerrainHeightCacheWindow StagingWindow;

    public readonly bool HasTargetWindow;
    public readonly TerrainHeightCacheWindow TargetWindow;

    public readonly TerrainAuthoringPreviewStreamingState StreamingState;
    public readonly string StreamingStatusMessage;

    public readonly float StreamingProgress;

    public readonly bool IsStreaming;
    public readonly bool WaitingForCoverage;

    public readonly int ActiveTileCount;

    public readonly int RetainedTileCount;
    public readonly int ReusableRetainedTileCount;
    public readonly int EnteringTileCount;
    public readonly int LeavingTileCount;

    public readonly int RetainedCopiedCount;
    public readonly int SourceLoadedCount;
    public readonly int SourceComposedCount;
    public readonly int SourceTileCount;

    public readonly long AuthoringGeneration;
    public readonly long StreamingRequestGeneration;

    public readonly long AnalysisResidencyGeneration;
    public readonly long AnalysisCompositeGeneration;

    public readonly long ActiveGpuMemoryBytes;
    public readonly long StagingGpuMemoryBytes;
    public readonly long CurrentResidentGpuMemoryBytes;
    public readonly long PeakTransitionGpuMemoryBytes;

    public readonly long CacheCreateCount;
    public readonly long CacheDisposeCount;
    public readonly int CacheLiveCount;

    public readonly bool EditorLifecycleStable;
    public readonly bool PreviewWorkAllowed;
    public readonly bool LifecycleResumePending;

    public readonly TerrainAuthoringPreviewSuspensionReason
        SuspensionReasons;

    public readonly bool HasControllingSceneView;
    public readonly int ControllingSceneViewInstanceId;
    public readonly long SceneViewOwnershipGeneration;

    public readonly bool FollowSceneView;
    public readonly TerrainAuthoringSceneViewFollowSource FollowSource;
    public readonly bool FreezePreview;

    public readonly bool HasTransitionFailure;
    public readonly TerrainHeightCacheWindow FailedWindow;
    public readonly string LastFailureMessage;

    public readonly bool FailureAffectsRequiredCoverage;

    internal TerrainAuthoringPreviewDiagnosticsSnapshot(
        bool enabled,
        bool cacheReady,
        TerrainAuthoringPreviewStatus previewStatus,
        string previewStatusMessage,
        bool hasActiveWindow,
        TerrainHeightCacheWindow activeWindow,
        bool hasRequiredWindow,
        TerrainHeightCacheWindow requiredWindow,
        bool hasDesiredWindow,
        TerrainHeightCacheWindow desiredWindow,
        bool hasRequestedWindow,
        TerrainHeightCacheWindow requestedWindow,
        bool hasStagingWindow,
        TerrainHeightCacheWindow stagingWindow,
        bool hasTargetWindow,
        TerrainHeightCacheWindow targetWindow,
        TerrainAuthoringPreviewStreamingState streamingState,
        string streamingStatusMessage,
        float streamingProgress,
        bool isStreaming,
        bool waitingForCoverage,
        int activeTileCount,
        int retainedTileCount,
        int reusableRetainedTileCount,
        int enteringTileCount,
        int leavingTileCount,
        int retainedCopiedCount,
        int sourceLoadedCount,
        int sourceComposedCount,
        int sourceTileCount,
        long authoringGeneration,
        long streamingRequestGeneration,
        long analysisResidencyGeneration,
        long analysisCompositeGeneration,
        long activeGpuMemoryBytes,
        long stagingGpuMemoryBytes,
        long currentResidentGpuMemoryBytes,
        long peakTransitionGpuMemoryBytes,
        long cacheCreateCount,
        long cacheDisposeCount,
        int cacheLiveCount,
        bool editorLifecycleStable,
        bool previewWorkAllowed,
        bool lifecycleResumePending,
        TerrainAuthoringPreviewSuspensionReason suspensionReasons,
        bool hasControllingSceneView,
        int controllingSceneViewInstanceId,
        long sceneViewOwnershipGeneration,
        bool followSceneView,
        TerrainAuthoringSceneViewFollowSource followSource,
        bool freezePreview,
        bool hasTransitionFailure,
        TerrainHeightCacheWindow failedWindow,
        string lastFailureMessage,
        bool failureAffectsRequiredCoverage
    )
    {
        Enabled = enabled;
        CacheReady = cacheReady;

        PreviewStatus = previewStatus;
        PreviewStatusMessage = previewStatusMessage ?? "";

        HasActiveWindow = hasActiveWindow;
        ActiveWindow = activeWindow;

        HasRequiredWindow = hasRequiredWindow;
        RequiredWindow = requiredWindow;

        HasDesiredWindow = hasDesiredWindow;
        DesiredWindow = desiredWindow;

        HasRequestedWindow = hasRequestedWindow;
        RequestedWindow = requestedWindow;

        HasStagingWindow = hasStagingWindow;
        StagingWindow = stagingWindow;

        HasTargetWindow = hasTargetWindow;
        TargetWindow = targetWindow;

        StreamingState = streamingState;
        StreamingStatusMessage = streamingStatusMessage ?? "";

        StreamingProgress = streamingProgress;

        IsStreaming = isStreaming;
        WaitingForCoverage = waitingForCoverage;

        ActiveTileCount = activeTileCount;

        RetainedTileCount = retainedTileCount;
        ReusableRetainedTileCount = reusableRetainedTileCount;
        EnteringTileCount = enteringTileCount;
        LeavingTileCount = leavingTileCount;

        RetainedCopiedCount = retainedCopiedCount;
        SourceLoadedCount = sourceLoadedCount;
        SourceComposedCount = sourceComposedCount;
        SourceTileCount = sourceTileCount;

        AuthoringGeneration = authoringGeneration;
        StreamingRequestGeneration = streamingRequestGeneration;

        AnalysisResidencyGeneration = analysisResidencyGeneration;
        AnalysisCompositeGeneration = analysisCompositeGeneration;

        ActiveGpuMemoryBytes = activeGpuMemoryBytes;
        StagingGpuMemoryBytes = stagingGpuMemoryBytes;
        CurrentResidentGpuMemoryBytes = currentResidentGpuMemoryBytes;
        PeakTransitionGpuMemoryBytes = peakTransitionGpuMemoryBytes;

        CacheCreateCount = cacheCreateCount;
        CacheDisposeCount = cacheDisposeCount;
        CacheLiveCount = cacheLiveCount;

        EditorLifecycleStable = editorLifecycleStable;
        PreviewWorkAllowed = previewWorkAllowed;
        LifecycleResumePending = lifecycleResumePending;

        SuspensionReasons = suspensionReasons;

        HasControllingSceneView = hasControllingSceneView;
        ControllingSceneViewInstanceId = controllingSceneViewInstanceId;
        SceneViewOwnershipGeneration = sceneViewOwnershipGeneration;

        FollowSceneView = followSceneView;
        FollowSource = followSource;
        FreezePreview = freezePreview;

        HasTransitionFailure = hasTransitionFailure;
        FailedWindow = failedWindow;
        LastFailureMessage = lastFailureMessage ?? "";

        FailureAffectsRequiredCoverage = failureAffectsRequiredCoverage;
    }
}
