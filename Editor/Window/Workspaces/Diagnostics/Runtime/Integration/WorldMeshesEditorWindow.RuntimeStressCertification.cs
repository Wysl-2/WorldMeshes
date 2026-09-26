using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    [SerializeField]
    private bool showRuntimeStressCertification;

    private void DrawRuntimeStressCertification(
        TerrainHeightmapStreamer streamer,
        TerrainClipmapDisplacementValidator validator,
        bool anyValidationRunning
    )
    {
        showRuntimeStressCertification =
            EditorGUILayout.Foldout(
                showRuntimeStressCertification,
                "Runtime Stress Certification",
                true
            );

        if (!showRuntimeStressCertification)
        {
            return;
        }

        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        EditorGUILayout.HelpBox(
            "Runs the full boundary, continuous-movement, rapid-request, " +
            "repeated-transition, deferred-release, and streamer lifecycle " +
            "certification suite. This is intentionally heavier than the " +
            "focused runtime validations above.",
            MessageType.Info
        );

        TerrainHeightDeferredReleaseDiagnosticsSnapshot deferred =
            streamer.GetHeightDeferredReleaseDiagnostics();

        GUILayout.Label(
            "Deferred Height Source Releases",
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Pending Releases",
            deferred.PendingCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Pending Payload",
            FormatRuntimeValidationBytes(
                deferred.EstimatedPendingSourceBytes
            )
        );

        EditorGUILayout.LabelField(
            "Peak Pending Releases",
            deferred.PeakPendingCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Peak Pending Payload",
            FormatRuntimeValidationBytes(
                deferred.PeakEstimatedPendingSourceBytes
            )
        );

        EditorGUILayout.LabelField(
            "Enqueued / Released",
            $"{deferred.EnqueuedCount} / {deferred.ReleasedCount}"
        );

        EditorGUILayout.LabelField(
            "Forced Releases",
            deferred.ForcedReleaseCount.ToString()
        );

        EditorGUILayout.LabelField(
            "Fence Fallbacks",
            deferred.FenceFallbackCount.ToString()
        );

        GUILayout.Space(6f);

        EditorGUILayout.LabelField(
            "Overall Status",
            validator.RuntimeStressCertificationStatus.ToString()
        );

        TerrainRuntimeStressCertificationResult result =
            validator.RuntimeStressCertificationResult;

        if (result != null)
        {
            EditorGUILayout.LabelField(
                "Boundary Stress",
                result.BoundaryStressStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Continuous Movement",
                result.ContinuousMovementStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Rapid Supersession",
                result.RapidSupersessionStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Repeated Transitions",
                result.RepeatedTransitionStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Deferred Release",
                result.DeferredReleaseStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Streamer Lifecycle",
                result.StreamerLifecycleStatus.ToString()
            );

            EditorGUILayout.LabelField(
                "Final Restore",
                result.FinalRestoreStatus.ToString()
            );

            EditorGUILayout.HelpBox(
                result.BuildDiagnosticReport(),
                MessageTypeForRuntimeValidationStatus(
                    result.OverallStatus
                )
            );
        }
        else if (
            !string.IsNullOrEmpty(
                validator.RuntimeStressCertificationSummary
            )
        )
        {
            EditorGUILayout.HelpBox(
                validator.RuntimeStressCertificationSummary,
                MessageTypeForRuntimeValidationStatus(
                    validator.RuntimeStressCertificationStatus
                )
            );
        }

        EditorGUI.BeginDisabledGroup(
            anyValidationRunning
        );

        if (
            GUILayout.Button(
                "Run Full Runtime Stress Certification",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            validator.BeginRuntimeStressCertification();
            Repaint();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.EndVertical();
    }
}
