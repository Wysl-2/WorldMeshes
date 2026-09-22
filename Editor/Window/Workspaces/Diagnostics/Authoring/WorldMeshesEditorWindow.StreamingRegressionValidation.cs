using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private bool streamingResourceStressCaptureActive;

    private long streamingResourceStressBaselineCreateCount;

    private long streamingResourceStressBaselineDisposeCount;

    private int streamingResourceStressBaselineLiveCount;

    private string streamingResourceStressSummary =
        "No resource stress capture is active.";

    private MessageType streamingResourceStressMessageType =
        MessageType.None;

    private void DrawStreamingRegressionValidationSettings()
    {
        bool requested =
            DrawValidationAction(
                "Final Streaming Regression",
                "Validation",
                TerrainAuthoringStreamingRegressionValidationUtility
                    .IsRunning,
                "Validate Final Streaming Architecture",
                "Runs final integration regression coverage for synthetic 32x32, 128x128, 512x512 and 1024x1024 logical height-tile worlds, bounded transition memory, all world edges/corners, one-tile movement cost, latest-target replacement, reversal, distant jumps, live Terrain Analysis source coherence and live preview-cache ownership.\n\nThe deterministic tests do not move the Scene View and do not allocate giant synthetic GPU caches. Lifecycle and Play Mode ownership remain manual regression steps.",
                MessageType.Info
            );

        if (requested)
        {
            TerrainAuthoringStreamingRegressionValidationUtility
                .ValidateStreamingRegression();
        }

        DrawWorkspaceSectionGap();

        DrawStreamingResourceStressCapture();

        DrawWorkspaceSectionGap();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Manual Lifecycle / Ownership Regression",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Use the live Streaming Preview State diagnostics while performing these checks:\n\n" +
            "• Start staging, then trigger compilation. Streaming should pause while the healthy active cache remains resident, then current intent should be reevaluated.\n\n" +
            "• Start staging, then trigger assembly/domain reload. Editor preview resources should be released and reconstructed without stranded caches.\n\n" +
            "• Disable Height Preview during staging. Live cache count should settle at 0. Re-enable and allow a fresh local cache to settle at 1.\n\n" +
            "• Change active scene during staging. Previous-scene active/staging resources must not survive.\n\n" +
            "• Switch between two Scene Views during staging, then close/reopen views. Ownership generation should change and obsolete targets must not return.\n\n" +
            "• Enter Play Mode during staging. Editor live cache count should reach 0 while TerrainHeightmapStreamer owns runtime residency. Returning to Edit Mode should reconstruct one local editor cache when Height Preview is enabled.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }

    private void DrawStreamingResourceStressCapture()
    {
        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot =
            TerrainAuthoringPreviewService
                .GetDiagnosticsSnapshot();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Repeated Transition / Resource Stress",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Capture",
            streamingResourceStressCaptureActive
                ? "Active"
                : "Inactive"
        );

        EditorGUILayout.LabelField(
            "Current Created",
            snapshot.CacheCreateCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Current Disposed",
            snapshot.CacheDisposeCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Current Live",
            snapshot.CacheLiveCount.ToString("N0")
        );

        GUILayout.Space(5f);

        EditorGUI.BeginDisabledGroup(
            Application.isPlaying
            ||
            EditorApplication.isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Begin Resource Stress Capture",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            streamingResourceStressBaselineCreateCount =
                snapshot.CacheCreateCount;

            streamingResourceStressBaselineDisposeCount =
                snapshot.CacheDisposeCount;

            streamingResourceStressBaselineLiveCount =
                snapshot.CacheLiveCount;

            streamingResourceStressCaptureActive =
                true;

            streamingResourceStressSummary =
                "Capture started. Move rapidly through the world, reverse direction, perform distant jumps, allow transitions to complete/cancel, then let streaming settle and validate the capture.";

            streamingResourceStressMessageType =
                MessageType.Info;
        }

        EditorGUI.BeginDisabledGroup(
            !streamingResourceStressCaptureActive
        );

        if (
            GUILayout.Button(
                "Validate Resource Stress Capture",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            ValidateStreamingResourceStressCapture(
                snapshot
            );
        }

        EditorGUI.EndDisabledGroup();
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(5f);

        EditorGUILayout.HelpBox(
            streamingResourceStressSummary,
            streamingResourceStressMessageType
        );

        GUILayout.EndVertical();
    }

    private void ValidateStreamingResourceStressCapture(
        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot
    )
    {
        if (
            snapshot.IsStreaming
            ||
            snapshot.HasStagingWindow
        )
        {
            streamingResourceStressSummary =
                "BLOCKED - streaming has not settled yet. Wait until no staging cache remains, then validate again.";

            streamingResourceStressMessageType =
                MessageType.Warning;

            return;
        }

        long createdDelta =
            snapshot.CacheCreateCount -
            streamingResourceStressBaselineCreateCount;

        long disposedDelta =
            snapshot.CacheDisposeCount -
            streamingResourceStressBaselineDisposeCount;

        int expectedLive =
            snapshot.CacheReady
                ? 1
                : 0;

        bool passed =
            snapshot.CacheLiveCount ==
                expectedLive
            &&
            snapshot.CacheLiveCount <=
                1;

        streamingResourceStressSummary =
            (
                passed
                    ? "PASS"
                    : "FAIL"
            )
            +
            $" - Created delta={createdDelta:N0}; " +
            $"Disposed delta={disposedDelta:N0}; " +
            $"Live before={streamingResourceStressBaselineLiveCount:N0}; " +
            $"Live after={snapshot.CacheLiveCount:N0}; " +
            $"Expected settled live={expectedLive:N0}.";

        streamingResourceStressMessageType =
            passed
                ? MessageType.Info
                : MessageType.Error;

        if (passed)
        {
            streamingResourceStressCaptureActive =
                false;
        }
    }
}
