using System;
using UnityEditor;
using UnityEngine;

/*
 * Package 08B non-modal Scene View feedback.
 *
 * This class observes PreviewService state only. It never requests residency,
 * mutates cache/lifecycle state, changes Scene View ownership, or retries
 * failed transitions.
 */
[InitializeOnLoad]
internal static class TerrainAuthoringPreviewStreamingOverlay
{
    internal const double LoadingFeedbackDelaySeconds =
        0.20;

    private const float OverlayWidth =
        350f;

    private const float OverlayMargin =
        10f;

    private const float OverlayTop =
        30f;

    private const float LoadingOverlayHeight =
        92f;

    private const float FailureOverlayHeight =
        150f;

    private static bool callbacksRegistered;

    private static double waitingForCoverageSince =
        double.NaN;

    private static long waitingRequestGeneration =
        -1L;

    static TerrainAuthoringPreviewStreamingOverlay()
    {
        RegisterCallbacks();
    }

    private static void RegisterCallbacks()
    {
        if (callbacksRegistered)
        {
            return;
        }

        callbacksRegistered =
            true;

        SceneView.duringSceneGui +=
            OnSceneViewGUI;

        TerrainAuthoringPreviewService
            .StreamingStateChanged +=
                OnPreviewStateChanged;

        TerrainAuthoringPreviewService
            .PreviewStateChanged +=
                OnPreviewStateChanged;

        TerrainAuthoringPreviewService
            .HeightCacheCoverageChanged +=
                OnPreviewStateChanged;

        TerrainAuthoringPreviewService
            .HeightCacheTransitionFailed +=
                OnHeightCacheTransitionFailed;

        AssemblyReloadEvents.beforeAssemblyReload +=
            Shutdown;

        EditorApplication.quitting +=
            Shutdown;
    }

    private static void UnregisterCallbacks()
    {
        if (!callbacksRegistered)
        {
            return;
        }

        callbacksRegistered =
            false;

        SceneView.duringSceneGui -=
            OnSceneViewGUI;

        TerrainAuthoringPreviewService
            .StreamingStateChanged -=
                OnPreviewStateChanged;

        TerrainAuthoringPreviewService
            .PreviewStateChanged -=
                OnPreviewStateChanged;

        TerrainAuthoringPreviewService
            .HeightCacheCoverageChanged -=
                OnPreviewStateChanged;

        TerrainAuthoringPreviewService
            .HeightCacheTransitionFailed -=
                OnHeightCacheTransitionFailed;

        AssemblyReloadEvents.beforeAssemblyReload -=
            Shutdown;

        EditorApplication.quitting -=
            Shutdown;
    }

    private static void Shutdown()
    {
        UnregisterCallbacks();

        ResetLoadingDelay();
    }

    private static void OnPreviewStateChanged()
    {
        SceneView.RepaintAll();
    }

    private static void OnHeightCacheTransitionFailed(
        TerrainHeightCacheWindow failedWindow,
        string failureMessage
    )
    {
        SceneView.RepaintAll();
    }

    private static void OnSceneViewGUI(
        SceneView sceneView
    )
    {
        if (
            sceneView == null
            ||
            sceneView.camera == null
        )
        {
            return;
        }

        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot =
            TerrainAuthoringPreviewService
                .GetDiagnosticsSnapshot();

        if (
            !snapshot.HasControllingSceneView
            ||
            snapshot.ControllingSceneViewInstanceId !=
                sceneView.GetInstanceID()
        )
        {
            return;
        }

        if (
            TerrainAuthoringPreviewFeedbackPolicy
                .ShouldShowFailure(
                    snapshot.HasTransitionFailure,
                    snapshot.FailureAffectsRequiredCoverage
                )
        )
        {
            ResetLoadingDelay();

            DrawFailureOverlay(
                sceneView,
                snapshot
            );

            return;
        }

        if (!snapshot.WaitingForCoverage)
        {
            ResetLoadingDelay();

            return;
        }

        if (
            waitingRequestGeneration !=
                snapshot.StreamingRequestGeneration
        )
        {
            waitingRequestGeneration =
                snapshot.StreamingRequestGeneration;

            waitingForCoverageSince =
                EditorApplication.timeSinceStartup;
        }
        else if (
            double.IsNaN(
                waitingForCoverageSince
            )
        )
        {
            waitingForCoverageSince =
                EditorApplication.timeSinceStartup;
        }

        double waitingDuration =
            EditorApplication.timeSinceStartup
            -
            waitingForCoverageSince;

        if (
            !TerrainAuthoringPreviewFeedbackPolicy
                .ShouldShowLoading(
                    snapshot.WaitingForCoverage,
                    waitingDuration,
                    LoadingFeedbackDelaySeconds
                )
        )
        {
            return;
        }

        DrawLoadingOverlay(
            sceneView,
            snapshot
        );
    }

    private static void DrawLoadingOverlay(
        SceneView sceneView,
        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot
    )
    {
        Rect area =
            CalculateOverlayRect(
                sceneView,
                LoadingOverlayHeight
            );

        const float padding =
            8f;

        const float rowHeight =
            18f;

        Rect contentRect =
            new Rect(
                area.x + padding,
                area.y + padding,
                Mathf.Max(
                    0f,
                    area.width -
                        (padding * 2f)
                ),
                Mathf.Max(
                    0f,
                    area.height -
                        (padding * 2f)
                )
            );

        Rect titleRect =
            new Rect(
                contentRect.x,
                contentRect.y,
                contentRect.width,
                rowHeight
            );

        Rect statusRect =
            new Rect(
                contentRect.x,
                titleRect.yMax + 4f,
                contentRect.width,
                rowHeight
            );

        Rect progressRect =
            new Rect(
                contentRect.x,
                statusRect.yMax + 6f,
                contentRect.width,
                rowHeight
            );

        string statusText =
            snapshot.SourceTileCount > 0
                ? $"Prepared {snapshot.SourceComposedCount:N0} / " +
                    $"{snapshot.SourceTileCount:N0} tiles"
                : "Preparing resident terrain...";

        Handles.BeginGUI();

        /*
         * This overlay can appear or disappear between IMGUI Layout and
         * Repaint events as streaming state advances. Explicit Rect-based GUI
         * calls avoid GUILayout's requirement that both passes build the same
         * control tree.
         */
        GUI.Box(
            area,
            GUIContent.none,
            EditorStyles.helpBox
        );

        GUI.Label(
            titleRect,
            "Loading terrain preview...",
            EditorStyles.boldLabel
        );

        GUI.Label(
            statusRect,
            statusText
        );

        EditorGUI.ProgressBar(
            progressRect,
            Mathf.Clamp01(
                snapshot.StreamingProgress
            ),
            snapshot.StreamingProgress.ToString("P0")
        );

        Handles.EndGUI();
    }

    private static void DrawFailureOverlay(
        SceneView sceneView,
        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot
    )
    {
        Rect area =
            CalculateOverlayRect(
                sceneView,
                FailureOverlayHeight
            );

        string failureMessage =
            string.IsNullOrEmpty(
                snapshot.LastFailureMessage
            )
                ? "The requested terrain preview transition failed."
                : snapshot.LastFailureMessage;

        string preservationMessage =
            snapshot.HasActiveWindow
                ? "Previous resident terrain remains active."
                : "No replacement terrain cache was activated.";

        string message =
            "Terrain preview could not load this area.\n\n" +
            preservationMessage +
            "\n\n" +
            failureMessage;

        Handles.BeginGUI();

        /*
         * Keep failure feedback independent from GUILayout for the same
         * Layout/Repaint consistency reason as the loading overlay.
         */
        EditorGUI.HelpBox(
            area,
            message,
            MessageType.Error
        );

        Handles.EndGUI();
    }

    private static Rect CalculateOverlayRect(
        SceneView sceneView,
        float height
    )
    {
        float width =
            Mathf.Min(
                OverlayWidth,
                Mathf.Max(
                    220f,
                    sceneView.position.width -
                        (OverlayMargin * 2f)
                )
            );

        float x =
            Mathf.Max(
                OverlayMargin,
                sceneView.position.width -
                    width -
                    OverlayMargin
            );

        return
            new Rect(
                x,
                OverlayTop,
                width,
                height
            );
    }

    private static void ResetLoadingDelay()
    {
        waitingForCoverageSince =
            double.NaN;

        waitingRequestGeneration =
            -1L;
    }
}
