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

        Handles.BeginGUI();

        GUILayout.BeginArea(
            area,
            EditorStyles.helpBox
        );

        GUILayout.Label(
            "Loading terrain preview...",
            EditorStyles.boldLabel
        );

        if (snapshot.SourceTileCount > 0)
        {
            EditorGUILayout.LabelField(
                $"Prepared {snapshot.SourceComposedCount:N0} / " +
                $"{snapshot.SourceTileCount:N0} tiles"
            );
        }
        else
        {
            EditorGUILayout.LabelField(
                "Preparing resident terrain..."
            );
        }

        Rect progressRect =
            GUILayoutUtility.GetRect(
                10f,
                18f,
                GUILayout.ExpandWidth(true)
            );

        EditorGUI.ProgressBar(
            progressRect,
            Mathf.Clamp01(
                snapshot.StreamingProgress
            ),
            snapshot.StreamingProgress.ToString("P0")
        );

        GUILayout.EndArea();

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

        Handles.BeginGUI();

        GUILayout.BeginArea(
            area
        );

        EditorGUILayout.HelpBox(
            "Terrain preview could not load this area.\n\n" +
            preservationMessage +
            "\n\n" +
            failureMessage,
            MessageType.Error
        );

        GUILayout.EndArea();

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
