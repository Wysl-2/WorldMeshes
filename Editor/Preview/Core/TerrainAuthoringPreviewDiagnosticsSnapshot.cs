using System;
using System.Collections.Generic;
using UnityEngine;

// Copied values only: diagnostics never own Height resources or advance work.

internal readonly struct TerrainAuthoringPreviewRepresentationSnapshot
{
    public readonly int Stride;
    public readonly int SamplesPerSide;
    public readonly float SampleSpacing;
    public bool IsValid => Stride > 0 && SamplesPerSide > 1 && SampleSpacing > 0f;

    internal TerrainAuthoringPreviewRepresentationSnapshot(
        int stride,
        int samplesPerSide,
        float sampleSpacing)
    {
        Stride = stride;
        SamplesPerSide = samplesPerSide;
        SampleSpacing = sampleSpacing;
    }
}

internal readonly struct TerrainAuthoringPreviewCacheSnapshot
{
    public readonly bool Present;
    public readonly bool HasTexture;
    public readonly TerrainAuthoringPreviewRepresentationSnapshot Representation;
    public readonly TerrainHeightCacheWindow Window;
    public readonly int TextureId;
    public readonly int PageCount;
    public readonly bool Complete;
    public readonly bool Current;
    public readonly long AuthoringGeneration;
    public readonly long GpuBytes;

    internal TerrainAuthoringPreviewCacheSnapshot(
        bool present,
        bool hasTexture,
        TerrainAuthoringPreviewRepresentationSnapshot representation,
        TerrainHeightCacheWindow window,
        int textureId,
        int pageCount,
        bool complete,
        bool current,
        long authoringGeneration,
        long gpuBytes)
    {
        Present = present;
        HasTexture = hasTexture;
        Representation = representation;
        Window = window;
        TextureId = textureId;
        PageCount = pageCount;
        Complete = complete;
        Current = current;
        AuthoringGeneration = authoringGeneration;
        GpuBytes = gpuBytes;
    }
}

internal readonly struct TerrainAuthoringPreviewDirtyFailureSnapshot
{
    public readonly bool Present;
    public readonly Vector2Int Tile;
    public readonly int Level;
    public readonly long AttemptedGeneration;
    public readonly long AttemptSequence;
    public readonly string Message;
    public readonly bool LastGoodAvailable;

    internal TerrainAuthoringPreviewDirtyFailureSnapshot(Vector2Int tile, TerrainAuthoringPreviewDirtyFailure failure, int level = -1)
    {
        Present = true; Tile = tile; Level = level; AttemptedGeneration = failure.AttemptedGeneration;
        AttemptSequence = failure.AttemptSequence;
        Message = failure.Message; LastGoodAvailable = failure.LastGoodAvailable;
    }
}

internal readonly struct TerrainAuthoringPreviewDirtySourceSnapshot
{
    public readonly bool Present;
    public readonly Vector2Int Tile;
    public readonly string CommittedSignature;
    public readonly int SettingsId;
    public readonly int NativeSamples;
    public readonly int TextureId;
    public readonly long ApproximatePayloadBytes;

    internal TerrainAuthoringPreviewDirtySourceSnapshot(bool present, Vector2Int tile, string committed,
        int settings, int samples, int texture, long bytes)
    {
        Present = present; Tile = tile; CommittedSignature = committed ?? "";
        SettingsId = settings; NativeSamples = samples; TextureId = texture; ApproximatePayloadBytes = bytes;
    }
}

internal readonly struct TerrainAuthoringPreviewLodDiagnosticsSnapshot
{
    public readonly int Level;
    public readonly bool HasLatestPlan;
    public readonly TerrainAuthoringPreviewRepresentationSnapshot PlannedRepresentation;
    public readonly TerrainAuthoringPreviewCacheSnapshot Active;
    public readonly TerrainAuthoringPreviewCacheSnapshot Staging;
    public readonly TerrainHeightCacheWindow PublishedRequiredWindow;
    public readonly TerrainHeightCacheWindow RequiredWindow;
    public readonly TerrainHeightCacheWindow GuardedWindow;
    public readonly TerrainHeightCacheWindow DesiredWindow;
    public readonly TerrainHeightCacheWindow WorkerRequiredWindow;
    public readonly TerrainHeightCacheWindow WorkerTargetWindow;
    public readonly TerrainAuthoringPreviewRepresentationSnapshot WorkerRepresentation;
    public readonly TerrainHeightCacheWindow QueuedTargetWindow;
    public readonly TerrainAuthoringPreviewRepresentationSnapshot QueuedRepresentation;
    public readonly int PendingDirtyCount;
    public readonly int FailedDirtyCount;
    public readonly TerrainAuthoringPreviewDirtyFailureSnapshot DirtyFailure;
    public readonly long DirtyScratchBytes;
    public readonly bool WriteFailed;
    public readonly long PublishedGeneration;
    public readonly long RequestGeneration;
    public readonly TerrainAuthoringPreviewTransitionState Phase;
    public readonly int RetainedCount;
    public readonly int ReusableCount;
    public readonly int EnteringCount;
    public readonly int LeavingCount;
    public readonly int CopiedCount;
    public readonly int MaterializedCount;
    public readonly int CompositionRemainingCount;
    public readonly int ComposedCount;
    public readonly TerrainAuthoringPreviewResidencySizeHealth SizeHealth;

    internal TerrainAuthoringPreviewLodDiagnosticsSnapshot(
        int level,
        bool hasLatestPlan,
        TerrainAuthoringPreviewRepresentationSnapshot plannedRepresentation,
        TerrainAuthoringPreviewCacheSnapshot active,
        TerrainAuthoringPreviewCacheSnapshot staging,
        TerrainHeightCacheWindow publishedRequiredWindow,
        TerrainHeightCacheWindow requiredWindow,
        TerrainHeightCacheWindow guardedWindow,
        TerrainHeightCacheWindow desiredWindow,
        TerrainHeightCacheWindow workerRequiredWindow,
        TerrainHeightCacheWindow workerTargetWindow,
        TerrainAuthoringPreviewRepresentationSnapshot workerRepresentation,
        TerrainHeightCacheWindow queuedTargetWindow,
        TerrainAuthoringPreviewRepresentationSnapshot queuedRepresentation,
        int pendingDirtyCount,
        bool writeFailed,
        long publishedGeneration,
        long requestGeneration,
        TerrainAuthoringPreviewTransitionState phase,
        int retainedCount,
        int reusableCount,
        int enteringCount,
        int leavingCount,
        int copiedCount,
        int materializedCount,
        int compositionRemainingCount,
        int composedCount,
        TerrainAuthoringPreviewResidencySizeHealth sizeHealth,
        int failedDirtyCount = 0,
        TerrainAuthoringPreviewDirtyFailureSnapshot dirtyFailure = default,
        long dirtyScratchBytes = 0L)
    {
        Level = level;
        HasLatestPlan = hasLatestPlan;
        PlannedRepresentation = plannedRepresentation;
        Active = active;
        Staging = staging;
        PublishedRequiredWindow = publishedRequiredWindow;
        RequiredWindow = requiredWindow;
        GuardedWindow = guardedWindow;
        DesiredWindow = desiredWindow;
        WorkerRequiredWindow = workerRequiredWindow;
        WorkerTargetWindow = workerTargetWindow;
        WorkerRepresentation = workerRepresentation;
        QueuedTargetWindow = queuedTargetWindow;
        QueuedRepresentation = queuedRepresentation;
        PendingDirtyCount = pendingDirtyCount;
        FailedDirtyCount = failedDirtyCount; DirtyFailure = dirtyFailure; DirtyScratchBytes = dirtyScratchBytes;
        WriteFailed = writeFailed;
        PublishedGeneration = publishedGeneration;
        RequestGeneration = requestGeneration;
        Phase = phase;
        RetainedCount = retainedCount;
        ReusableCount = reusableCount;
        EnteringCount = enteringCount;
        LeavingCount = leavingCount;
        CopiedCount = copiedCount;
        MaterializedCount = materializedCount;
        CompositionRemainingCount = compositionRemainingCount;
        ComposedCount = composedCount;
        SizeHealth = sizeHealth;
    }
}

internal readonly struct TerrainAuthoringPreviewWorkerSnapshot
{
    public readonly bool Present;
    public readonly TerrainAuthoringPreviewCachePublication Purpose;
    public readonly TerrainAuthoringPreviewTransitionState Phase;
    public readonly long RequestGeneration;
    public readonly long AuthoringGeneration;
    public readonly long OwnershipGeneration;
    public readonly float Progress;
    public readonly int SourceGroupCount;
    public readonly int LoadedGroupCount;
    public readonly int RepresentationCount;
    public readonly int MaterializedCount;
    public readonly int ComposedCount;
    public readonly int ReusableCount;
    public readonly int CopiedCount;
    public readonly int LastAllocations;
    public readonly int LastLoads;
    public readonly int LastCopies;
    public readonly int LastMaterializations;
    public readonly int LastCompositions;

    internal TerrainAuthoringPreviewWorkerSnapshot(
        bool present,
        TerrainAuthoringPreviewCachePublication purpose,
        TerrainAuthoringPreviewTransitionState phase,
        long requestGeneration,
        long authoringGeneration,
        long ownershipGeneration,
        float progress,
        int sourceGroupCount,
        int loadedGroupCount,
        int representationCount,
        int materializedCount,
        int composedCount,
        int reusableCount,
        int copiedCount,
        int lastAllocations,
        int lastLoads,
        int lastCopies,
        int lastMaterializations,
        int lastCompositions)
    {
        Present = present;
        Purpose = purpose;
        Phase = phase;
        RequestGeneration = requestGeneration;
        AuthoringGeneration = authoringGeneration;
        OwnershipGeneration = ownershipGeneration;
        Progress = progress;
        SourceGroupCount = sourceGroupCount;
        LoadedGroupCount = loadedGroupCount;
        RepresentationCount = representationCount;
        MaterializedCount = materializedCount;
        ComposedCount = composedCount;
        ReusableCount = reusableCount;
        CopiedCount = copiedCount;
        LastAllocations = lastAllocations;
        LastLoads = lastLoads;
        LastCopies = lastCopies;
        LastMaterializations = lastMaterializations;
        LastCompositions = lastCompositions;
    }
}

internal readonly struct TerrainAuthoringPreviewOwnershipSnapshot
{
    public readonly long DisplayActiveBytes;
    public readonly long AnalysisActiveBytes;
    public readonly long DisplayStagingBytes;
    public readonly long AnalysisStagingBytes;
    public readonly long RetiringDisplayBytes;
    public readonly long RetiringAnalysisBytes;
    public readonly long DirtyScratchBytes;
    public readonly int DirtyScratchArrayCount;
    public readonly int OwnedCacheCount;
    public readonly int AllocatedArrayCount;
    public readonly int DisplayActiveCount;
    public readonly int AnalysisActiveCount;
    public readonly int DisplayStagingCount;
    public readonly int AnalysisStagingCount;
    public readonly int RetiringCount;
    public long ActiveBytes => DisplayActiveBytes + AnalysisActiveBytes;
    public long StagingBytes => DisplayStagingBytes + AnalysisStagingBytes;
    public long TotalBytes => ActiveBytes + StagingBytes + RetiringDisplayBytes + RetiringAnalysisBytes + DirtyScratchBytes;

    internal TerrainAuthoringPreviewOwnershipSnapshot(
        long displayActiveBytes,
        long analysisActiveBytes,
        long displayStagingBytes,
        long analysisStagingBytes,
        long retiringDisplayBytes,
        long retiringAnalysisBytes,
        int ownedCacheCount,
        int allocatedArrayCount,
        int displayActiveCount,
        int analysisActiveCount,
        int displayStagingCount,
        int analysisStagingCount,
        int retiringCount,
        long dirtyScratchBytes = 0L,
        int dirtyScratchArrayCount = 0)
    {
        DisplayActiveBytes = displayActiveBytes;
        AnalysisActiveBytes = analysisActiveBytes;
        DisplayStagingBytes = displayStagingBytes;
        AnalysisStagingBytes = analysisStagingBytes;
        RetiringDisplayBytes = retiringDisplayBytes;
        RetiringAnalysisBytes = retiringAnalysisBytes;
        OwnedCacheCount = ownedCacheCount;
        AllocatedArrayCount = allocatedArrayCount;
        DisplayActiveCount = displayActiveCount;
        AnalysisActiveCount = analysisActiveCount;
        DisplayStagingCount = displayStagingCount;
        AnalysisStagingCount = analysisStagingCount;
        RetiringCount = retiringCount;
        DirtyScratchBytes = dirtyScratchBytes; DirtyScratchArrayCount = dirtyScratchArrayCount;
    }
}

internal readonly struct TerrainAuthoringPreviewFailureSnapshot
{
    public readonly bool Present;
    public readonly TerrainAuthoringPreviewCachePublication Purpose;
    public readonly int Level;
    public readonly bool HasTile;
    public readonly Vector2Int Tile;
    public readonly TerrainHeightCacheWindow Window;
    public readonly long RequestGeneration;
    public readonly long PlacementGeneration;
    public readonly string Message;

    internal TerrainAuthoringPreviewFailureSnapshot(
        bool present,
        TerrainAuthoringPreviewCachePublication purpose,
        int level,
        bool hasTile,
        Vector2Int tile,
        TerrainHeightCacheWindow window,
        long requestGeneration,
        long placementGeneration,
        string message)
    {
        Present = present;
        Purpose = purpose;
        Level = level;
        HasTile = hasTile;
        Tile = tile;
        Window = window;
        RequestGeneration = requestGeneration;
        PlacementGeneration = placementGeneration;
        Message = message ?? "";
    }
}

internal readonly struct TerrainAuthoringPreviewDiagnosticsSnapshot
{
    public readonly bool Enabled;
    // Complete authoring convergence; pending content does not invalidate residency.
    public readonly bool CacheReady;
    public readonly bool Drawable;
    // Physical representation/committed coverage and paired placement are independent.
    public readonly bool LatestCoverageCurrent;
    public readonly bool PlacementCurrent;
    // Latest physical destination is usable, even if content is still updating.
    public readonly bool ReadyForLatestIntent;
    public readonly TerrainAuthoringPreviewStatus PreviewStatus;
    public readonly string PreviewStatusMessage;
    public readonly TerrainAuthoringPreviewStreamingState StreamingState;
    public readonly string StreamingStatusMessage;
    public readonly float StreamingProgress;
    public readonly bool IsStreaming;
    public readonly bool WaitingForCoverage;
    public readonly long AuthoringGeneration;
    public readonly long StreamingRequestGeneration;
    public readonly TerrainAuthoringPreviewWorkerSnapshot Worker;
    public readonly TerrainAuthoringPreviewWorkerSnapshot QueuedWorker;
    public readonly TerrainAuthoringAnalysisSourceSnapshot Analysis;
    public readonly TerrainAuthoringPreviewCacheSnapshot AnalysisActive;
    public readonly TerrainAuthoringPreviewCacheSnapshot AnalysisStaging;
    public readonly TerrainAuthoringPreviewOwnershipSnapshot Ownership;
    public readonly TerrainAuthoringPreviewFailureSnapshot DisplayFailure;
    public readonly TerrainAuthoringPreviewFailureSnapshot AnalysisFailure;
    public readonly bool FailureAffectsRequiredCoverage;
    public readonly long PeakTransitionGpuMemoryBytes;
    public readonly long CacheCreateCount;
    public readonly long CacheDisposeCount;
    public readonly int CacheLiveCount;
    public readonly bool EditorLifecycleStable;
    public readonly bool PreviewWorkAllowed;
    public readonly bool LifecycleResumePending;
    public readonly TerrainAuthoringPreviewSuspensionReason SuspensionReasons;
    public readonly bool HasControllingSceneView;
    public readonly int ControllingSceneViewInstanceId;
    public readonly long SceneViewOwnershipGeneration;
    public readonly bool FollowSceneView;
    public readonly TerrainAuthoringSceneViewFollowSource FollowSource;
    public readonly bool FreezePreview;
    public readonly int PendingGeographicDirtyCount;
    public readonly int LastDirtyAllocations;
    public readonly int LastDirtyCopies;
    public readonly bool BoundsFollowUpPending;
    public readonly bool AnalysisFollowUpPending;
    public readonly string BoundsFollowUpError;
    public readonly string AnalysisFollowUpError;
    public readonly string LastFollowUpError;
    public readonly int PendingRepresentationCount;
    public readonly int FailedRepresentationCount;
    public readonly bool AuthoringConvergencePending;
    public readonly bool UnprojectedScopePending;
    public readonly int UnprojectedGeographicDirtyCount;
    public readonly TerrainAuthoringPreviewDirtyFailureSnapshot MostRecentDirtyFailure;
    public readonly TerrainAuthoringPreviewDirtySourceSnapshot HeldDirtySource;
    public readonly long LatestPlacementGeneration;
    public bool HasFailedDirtyUpdates => FailedRepresentationCount > 0;
    public readonly int LastDirtyLoads;
    public readonly int LastDirtyMaterializations;
    public readonly int LastDirtyCompositions;
    public readonly string CancellationReason;
    public readonly IReadOnlyList<TerrainAuthoringPreviewLodDiagnosticsSnapshot> DisplayLods;
    public bool HasTransitionFailure => DisplayFailure.Present;
    public string LastFailureMessage => DisplayFailure.Message ?? "";

    internal TerrainAuthoringPreviewDiagnosticsSnapshot(
        bool enabled,
        bool cacheReady,
        bool drawable,
        bool latestCoverageCurrent,
        bool placementCurrent,
        bool readyForLatestIntent,
        TerrainAuthoringPreviewStatus previewStatus,
        string previewStatusMessage,
        TerrainAuthoringPreviewStreamingState streamingState,
        string streamingStatusMessage,
        float streamingProgress,
        bool isStreaming,
        bool waitingForCoverage,
        long authoringGeneration,
        long streamingRequestGeneration,
        TerrainAuthoringPreviewWorkerSnapshot worker,
        TerrainAuthoringPreviewWorkerSnapshot queuedWorker,
        TerrainAuthoringAnalysisSourceSnapshot analysis,
        TerrainAuthoringPreviewCacheSnapshot analysisActive,
        TerrainAuthoringPreviewCacheSnapshot analysisStaging,
        TerrainAuthoringPreviewOwnershipSnapshot ownership,
        TerrainAuthoringPreviewFailureSnapshot displayFailure,
        TerrainAuthoringPreviewFailureSnapshot analysisFailure,
        bool failureAffectsRequiredCoverage,
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
        int pendingGeographicDirtyCount,
        int lastDirtyLoads,
        int lastDirtyMaterializations,
        int lastDirtyCompositions,
        string cancellationReason,
        TerrainAuthoringPreviewLodDiagnosticsSnapshot[] displayLods,
        int lastDirtyAllocations = 0,
        int lastDirtyCopies = 0,
        bool boundsFollowUpPending = false,
        bool analysisFollowUpPending = false,
        string boundsFollowUpError = "",
        string analysisFollowUpError = "",
        string lastFollowUpError = "",
        bool unprojectedScopePending = false,
        int unprojectedGeographicDirtyCount = 0,
        TerrainAuthoringPreviewDirtySourceSnapshot heldDirtySource = default,
        long latestPlacementGeneration = 0)
    {
        Enabled = enabled;
        CacheReady = cacheReady;
        Drawable = drawable;
        LatestCoverageCurrent = latestCoverageCurrent;
        PlacementCurrent = placementCurrent;
        ReadyForLatestIntent = readyForLatestIntent;
        PreviewStatus = previewStatus;
        PreviewStatusMessage = previewStatusMessage ?? "";
        StreamingState = streamingState;
        StreamingStatusMessage = streamingStatusMessage ?? "";
        StreamingProgress = streamingProgress;
        IsStreaming = isStreaming;
        WaitingForCoverage = waitingForCoverage;
        AuthoringGeneration = authoringGeneration;
        StreamingRequestGeneration = streamingRequestGeneration;
        Worker = worker;
        QueuedWorker = queuedWorker;
        Analysis = analysis;
        AnalysisActive = analysisActive;
        AnalysisStaging = analysisStaging;
        Ownership = ownership;
        DisplayFailure = displayFailure;
        AnalysisFailure = analysisFailure;
        FailureAffectsRequiredCoverage = failureAffectsRequiredCoverage;
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
        PendingGeographicDirtyCount = pendingGeographicDirtyCount;
        LastDirtyAllocations = lastDirtyAllocations; LastDirtyCopies = lastDirtyCopies;
        BoundsFollowUpPending = boundsFollowUpPending; AnalysisFollowUpPending = analysisFollowUpPending;
        BoundsFollowUpError = boundsFollowUpError ?? ""; AnalysisFollowUpError = analysisFollowUpError ?? "";
        LastFollowUpError = lastFollowUpError ?? "";
        LastDirtyLoads = lastDirtyLoads;
        LastDirtyMaterializations = lastDirtyMaterializations;
        LastDirtyCompositions = lastDirtyCompositions;
        CancellationReason = cancellationReason ?? "";
        DisplayLods = Array.AsReadOnly((TerrainAuthoringPreviewLodDiagnosticsSnapshot[])(displayLods ?? Array.Empty<TerrainAuthoringPreviewLodDiagnosticsSnapshot>()).Clone());
        int pending = 0, failed = 0;
        var recent = default(TerrainAuthoringPreviewDirtyFailureSnapshot);
        foreach (var row in DisplayLods)
        {
            pending += row.PendingDirtyCount; failed += row.FailedDirtyCount;
            if (TerrainAuthoringPreviewService.IsNewerDirtyFailure(row.DirtyFailure, recent)) recent = row.DirtyFailure;
        }
        PendingRepresentationCount = pending; FailedRepresentationCount = failed;
        UnprojectedScopePending = unprojectedScopePending;
        UnprojectedGeographicDirtyCount = unprojectedGeographicDirtyCount;
        AuthoringConvergencePending = pending > 0 || unprojectedScopePending;
        MostRecentDirtyFailure = recent; HeldDirtySource = heldDirtySource;
        LatestPlacementGeneration = latestPlacementGeneration;
    }
}

