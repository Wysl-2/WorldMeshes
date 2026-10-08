using System;
using UnityEditor;
using UnityEngine;

// Observes copied operational state; never requests terrain work or captures tool input.
[InitializeOnLoad]
internal static class TerrainAuthoringPreviewStreamingOverlay
{
    internal const double LoadingFeedbackDelaySeconds = 0.20;
    private const float OverlayWidth = 380f;
    private const float OverlayMargin = 10f;
    private const float OverlayTop = 30f;
    private static bool callbacksRegistered;
    private static double waitingForCoverageSince = double.NaN;
    private static long waitingRequestGeneration = -1L;
    private static long waitingPlacementGeneration = -1L;
    private static long waitingOwnershipGeneration = -1L;

    static TerrainAuthoringPreviewStreamingOverlay() { RegisterCallbacks(); }

    private static void RegisterCallbacks()
    {
        if (callbacksRegistered) return;
        callbacksRegistered = true;
        SceneView.duringSceneGui += OnSceneViewGUI;
        TerrainAuthoringPreviewService.StreamingStateChanged += OnPreviewStateChanged;
        TerrainAuthoringPreviewService.PreviewStateChanged += OnPreviewStateChanged;
        TerrainAuthoringPreviewService.HeightCacheCoverageChanged += OnPreviewStateChanged;
        TerrainAuthoringPreviewService.HeightCacheTransitionFailed += OnHeightCacheTransitionFailed;
        AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        EditorApplication.quitting += Shutdown;
    }

    private static void Shutdown()
    {
        if (callbacksRegistered)
        {
            callbacksRegistered = false;
            SceneView.duringSceneGui -= OnSceneViewGUI;
            TerrainAuthoringPreviewService.StreamingStateChanged -= OnPreviewStateChanged;
            TerrainAuthoringPreviewService.PreviewStateChanged -= OnPreviewStateChanged;
            TerrainAuthoringPreviewService.HeightCacheCoverageChanged -= OnPreviewStateChanged;
            TerrainAuthoringPreviewService.HeightCacheTransitionFailed -= OnHeightCacheTransitionFailed;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
        }
        ResetLoadingDelay();
    }

    private static void OnPreviewStateChanged() { SceneView.RepaintAll(); }
    private static void OnHeightCacheTransitionFailed(TerrainHeightCacheWindow window, string message) { SceneView.RepaintAll(); }

    private static void OnSceneViewGUI(SceneView sceneView)
    {
        if (sceneView == null || sceneView.camera == null) return;
        var snapshot = TerrainAuthoringPreviewService.GetDiagnosticsSnapshot();
        if (!snapshot.HasControllingSceneView
            || snapshot.ControllingSceneViewInstanceId != sceneView.GetInstanceID()) return;
        var kind = TerrainAuthoringPreviewFeedbackPolicy.SelectFeedbackKind(snapshot);
        if (kind == TerrainAuthoringPreviewFeedbackKind.ResidencyLoading)
        {
            long request = TerrainAuthoringPreviewFeedbackPolicy.HasDisplayProgress(snapshot.Worker)
                ? snapshot.Worker.RequestGeneration
                : TerrainAuthoringPreviewFeedbackPolicy.HasDisplayProgress(snapshot.QueuedWorker)
                    ? snapshot.QueuedWorker.RequestGeneration : 0L;
            if (waitingOwnershipGeneration != snapshot.SceneViewOwnershipGeneration
                || waitingPlacementGeneration != snapshot.LatestPlacementGeneration
                || request != 0 && request != waitingRequestGeneration || double.IsNaN(waitingForCoverageSince))
            {
                waitingForCoverageSince = EditorApplication.timeSinceStartup;
                waitingPlacementGeneration = snapshot.LatestPlacementGeneration;
                waitingOwnershipGeneration = snapshot.SceneViewOwnershipGeneration;
                waitingRequestGeneration = request;
            }
            if (!TerrainAuthoringPreviewFeedbackPolicy.ShouldShowLoading(true,
                EditorApplication.timeSinceStartup - waitingForCoverageSince, LoadingFeedbackDelaySeconds)) return;
            DrawLoadingOverlay(sceneView, snapshot); return;
        }
        ResetLoadingDelay();
        switch (kind)
        {
            case TerrainAuthoringPreviewFeedbackKind.ResidencyFailure:
                DrawMessage(sceneView, "Terrain preview could not load this area.\n"
                    + (snapshot.Drawable ? "Previous resident terrain remains active.\n" : "No replacement terrain was activated.\n")
                    + snapshot.LastFailureMessage, MessageType.Error, 124f); break;
            case TerrainAuthoringPreviewFeedbackKind.DirtyFailure:
                var failure = snapshot.MostRecentDirtyFailure;
                string preservation = failure.LastGoodAvailable && snapshot.Drawable
                    ? "Previous terrain remains active." : "Unsafe Height storage requires replacement.";
                DrawMessage(sceneView, $"Height update failed at LOD {failure.Level}, tile {failure.Tile}.\n{preservation}\n"
                    + $"{Math.Max(0, snapshot.PendingRepresentationCount - snapshot.FailedRepresentationCount):N0} other update(s) pending. "
                    + $"Retry through Height Preview or a relevant edit.\n{failure.Message}",
                    failure.LastGoodAvailable ? MessageType.Warning : MessageType.Error, 124f); break;
            case TerrainAuthoringPreviewFeedbackKind.Updating:
                DrawMessage(sceneView, $"Updating terrain preview — {snapshot.PendingRepresentationCount:N0} representation update(s), "
                    + $"{snapshot.PendingGeographicDirtyCount:N0} resident tile(s) pending."
                    + (snapshot.UnprojectedScopePending ? " New authoring scope is awaiting projection." : ""), MessageType.Info, 56f); break;
            case TerrainAuthoringPreviewFeedbackKind.Paused:
                DrawMessage(sceneView, "Terrain preview work is paused while the editor settles."
                    + (snapshot.Drawable ? " Resident terrain remains active." : "")
                    + $" Pending Height updates: {snapshot.PendingRepresentationCount:N0}.", MessageType.Info, 56f); break;
        }
    }

    private static void DrawLoadingOverlay(SceneView sceneView, TerrainAuthoringPreviewDiagnosticsSnapshot snapshot)
    {
        bool progress = TerrainAuthoringPreviewFeedbackPolicy.HasDisplayProgress(snapshot.Worker);
        string status = progress ? $"Prepared {snapshot.Worker.ComposedCount:N0} / {snapshot.Worker.RepresentationCount:N0} display pages"
            : snapshot.LatestCoverageCurrent && !snapshot.PlacementCurrent ? "Waiting for the current terrain placement."
            : snapshot.QueuedWorker.Present && snapshot.QueuedWorker.Purpose == TerrainAuthoringPreviewCachePublication.DisplayHeightSet
                ? "Display residency preparation is queued."
                : snapshot.Worker.Present ? "Display residency is waiting; current worker: " + snapshot.Worker.Purpose
                    : "Waiting for the current display residency request.";
        bool failure = snapshot.HasFailedDirtyUpdates;
        Rect area = CalculateOverlayRect(sceneView, (progress ? 94f : 70f) + (failure ? 30f : 0f));
        Handles.BeginGUI();
        try
        {
            // Rect-based drawing tolerates state changes between Layout and Repaint.
            GUI.Box(area, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(area.x + 8f, area.y + 8f, area.width - 16f, 18f),
                "Loading terrain preview...", EditorStyles.boldLabel);
            GUI.Label(new Rect(area.x + 8f, area.y + 30f, area.width - 16f, 18f), status);
            if (progress) EditorGUI.ProgressBar(new Rect(area.x + 8f, area.y + 54f, area.width - 16f, 18f),
                Mathf.Clamp01(snapshot.Worker.Progress), snapshot.Worker.Progress.ToString("P0"));
            if (failure) GUI.Label(new Rect(area.x + 8f, area.yMax - 26f, area.width - 16f, 18f),
                $"Separate Height updates failed: {snapshot.FailedRepresentationCount:N0}.");
        }
        finally { Handles.EndGUI(); }
    }

    private static void DrawMessage(SceneView sceneView, string text, MessageType type, float height)
    {
        Handles.BeginGUI();
        try { EditorGUI.HelpBox(CalculateOverlayRect(sceneView, height), text, type); }
        finally { Handles.EndGUI(); }
    }

    private static Rect CalculateOverlayRect(SceneView sceneView, float height)
    {
        float width = Mathf.Min(OverlayWidth, Mathf.Max(220f, sceneView.position.width - OverlayMargin * 2f));
        return new Rect(Mathf.Max(OverlayMargin, sceneView.position.width - width - OverlayMargin), OverlayTop, width, height);
    }

    private static void ResetLoadingDelay()
    {
        waitingForCoverageSince = double.NaN;
        waitingRequestGeneration = waitingPlacementGeneration = waitingOwnershipGeneration = -1L;
    }
}