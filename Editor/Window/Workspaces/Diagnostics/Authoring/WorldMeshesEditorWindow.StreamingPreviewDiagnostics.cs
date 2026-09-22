using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawStreamingPreviewDiagnostics()
    {
        TerrainAuthoringPreviewDiagnosticsSnapshot snapshot =
            TerrainAuthoringPreviewService
                .GetDiagnosticsSnapshot();

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Streaming Preview State",
            EditorStyles.boldLabel
        );

        GUILayout.Space(3f);

        GUILayout.Label(
            "Preview",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Enabled",
            FormatStreamingDiagnosticBool(
                snapshot.Enabled
            )
        );

        EditorGUILayout.LabelField(
            "Status",
            snapshot.PreviewStatus.ToString()
        );

        EditorGUILayout.LabelField(
            "Cache Ready",
            FormatStreamingDiagnosticBool(
                snapshot.CacheReady
            )
        );

        EditorGUILayout.LabelField(
            "Waiting For Coverage",
            FormatStreamingDiagnosticBool(
                snapshot.WaitingForCoverage
            )
        );

        if (
            !string.IsNullOrEmpty(
                snapshot.PreviewStatusMessage
            )
        )
        {
            EditorGUILayout.HelpBox(
                snapshot.PreviewStatusMessage,
                snapshot.PreviewStatus ==
                    TerrainAuthoringPreviewStatus.Error
                    ? MessageType.Error
                    : MessageType.Info
            );
        }

        DrawWorkspaceSectionGap();

        GUILayout.Label(
            "Residency",
            EditorStyles.boldLabel
        );

        DrawStreamingDiagnosticWindow(
            "Active Window",
            snapshot.HasActiveWindow,
            snapshot.ActiveWindow
        );

        DrawStreamingDiagnosticWindow(
            "Required Window",
            snapshot.HasRequiredWindow,
            snapshot.RequiredWindow
        );

        DrawStreamingDiagnosticWindow(
            "Desired Window",
            snapshot.HasDesiredWindow,
            snapshot.DesiredWindow
        );

        DrawStreamingDiagnosticWindow(
            "Requested Window",
            snapshot.HasRequestedWindow,
            snapshot.RequestedWindow
        );

        DrawStreamingDiagnosticWindow(
            "Staging Window",
            snapshot.HasStagingWindow,
            snapshot.StagingWindow
        );

        DrawStreamingDiagnosticWindow(
            "Target Window",
            snapshot.HasTargetWindow,
            snapshot.TargetWindow
        );

        EditorGUILayout.LabelField(
            "Active Tiles",
            snapshot.ActiveTileCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Retained Tiles",
            snapshot.RetainedTileCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Reusable Retained",
            snapshot.ReusableRetainedTileCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Entering Tiles",
            snapshot.EnteringTileCount.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Leaving Tiles",
            snapshot.LeavingTileCount.ToString("N0")
        );

        DrawWorkspaceSectionGap();

        GUILayout.Label(
            "Streaming",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "State",
            snapshot.StreamingState.ToString()
        );

        EditorGUILayout.LabelField(
            "Progress",
            snapshot.StreamingProgress.ToString("P1")
        );

        EditorGUILayout.LabelField(
            "Waiting For Coverage",
            FormatStreamingDiagnosticBool(
                snapshot.WaitingForCoverage
            )
        );

        EditorGUILayout.LabelField(
            "Request Generation",
            snapshot.StreamingRequestGeneration.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Retained Copies",
            $"{snapshot.RetainedCopiedCount:N0} / " +
            $"{snapshot.ReusableRetainedTileCount:N0}"
        );

        EditorGUILayout.LabelField(
            "Committed Loads",
            $"{snapshot.SourceLoadedCount:N0} / " +
            $"{snapshot.SourceTileCount:N0}"
        );

        EditorGUILayout.LabelField(
            "Composed Tiles",
            $"{snapshot.SourceComposedCount:N0} / " +
            $"{snapshot.SourceTileCount:N0}"
        );

        EditorGUILayout.LabelField(
            "Authoring Generation",
            snapshot.AuthoringGeneration.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Analysis Residency Generation",
            snapshot.AnalysisResidencyGeneration.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Analysis Composite Generation",
            snapshot.AnalysisCompositeGeneration.ToString("N0")
        );

        if (
            !string.IsNullOrEmpty(
                snapshot.StreamingStatusMessage
            )
        )
        {
            EditorGUILayout.HelpBox(
                snapshot.StreamingStatusMessage,
                snapshot.StreamingState ==
                    TerrainAuthoringPreviewStreamingState.Failed
                    ? MessageType.Error
                    : MessageType.Info
            );
        }

        DrawWorkspaceSectionGap();

        GUILayout.Label(
            "GPU Memory Estimates",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Active",
            FormatPreviewMemory(
                snapshot.ActiveGpuMemoryBytes
            )
        );

        EditorGUILayout.LabelField(
            "Staging",
            FormatPreviewMemory(
                snapshot.StagingGpuMemoryBytes
            )
        );

        EditorGUILayout.LabelField(
            "Current Total",
            FormatPreviewMemory(
                snapshot.CurrentResidentGpuMemoryBytes
            )
        );

        EditorGUILayout.LabelField(
            "Peak Transition",
            FormatPreviewMemory(
                snapshot.PeakTransitionGpuMemoryBytes
            )
        );

        DrawWorkspaceSectionGap();

        GUILayout.Label(
            "Lifecycle",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Editor Stable",
            FormatStreamingDiagnosticBool(
                snapshot.EditorLifecycleStable
            )
        );

        EditorGUILayout.LabelField(
            "Preview Work Allowed",
            FormatStreamingDiagnosticBool(
                snapshot.PreviewWorkAllowed
            )
        );

        EditorGUILayout.LabelField(
            "Resume Pending",
            FormatStreamingDiagnosticBool(
                snapshot.LifecycleResumePending
            )
        );

        EditorGUILayout.LabelField(
            "Suspension",
            snapshot.SuspensionReasons ==
                TerrainAuthoringPreviewSuspensionReason.None
                ? "None"
                : snapshot.SuspensionReasons.ToString()
        );

        DrawWorkspaceSectionGap();

        GUILayout.Label(
            "Scene View Ownership",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Has Owner",
            FormatStreamingDiagnosticBool(
                snapshot.HasControllingSceneView
            )
        );

        EditorGUILayout.LabelField(
            "Owner Instance ID",
            snapshot.ControllingSceneViewInstanceId.ToString()
        );

        EditorGUILayout.LabelField(
            "Ownership Generation",
            snapshot.SceneViewOwnershipGeneration.ToString("N0")
        );

        EditorGUILayout.LabelField(
            "Follow Enabled",
            FormatStreamingDiagnosticBool(
                snapshot.FollowSceneView
            )
        );

        EditorGUILayout.LabelField(
            "Follow Source",
            snapshot.FollowSource.ToString()
        );

        EditorGUILayout.LabelField(
            "Frozen",
            FormatStreamingDiagnosticBool(
                snapshot.FreezePreview
            )
        );

        if (snapshot.HasTransitionFailure)
        {
            DrawWorkspaceSectionGap();

            GUILayout.Label(
                "Last Transition Failure",
                EditorStyles.boldLabel
            );

            EditorGUILayout.LabelField(
                "Failed Window",
                snapshot.FailedWindow.IsValid
                    ? snapshot.FailedWindow.ToString()
                    : "(none)"
            );

            EditorGUILayout.LabelField(
                "Affects Required Coverage",
                FormatStreamingDiagnosticBool(
                    snapshot.FailureAffectsRequiredCoverage
                )
            );

            if (
                !string.IsNullOrEmpty(
                    snapshot.LastFailureMessage
                )
            )
            {
                EditorGUILayout.HelpBox(
                    snapshot.LastFailureMessage,
                    MessageType.Error
                );
            }
        }

        GUILayout.EndVertical();
    }

    private static void DrawStreamingDiagnosticWindow(
        string label,
        bool hasWindow,
        TerrainHeightCacheWindow window
    )
    {
        EditorGUILayout.LabelField(
            label,
            hasWindow
            &&
            window.IsValid
                ? window.ToString()
                : "(none)"
        );
    }

    private static string FormatStreamingDiagnosticBool(
        bool value
    )
    {
        return
            value
                ? "Yes"
                : "No";
    }
}
