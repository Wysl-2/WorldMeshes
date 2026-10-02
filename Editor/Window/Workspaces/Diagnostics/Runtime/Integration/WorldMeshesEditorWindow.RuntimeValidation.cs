using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow : EditorWindow
{
    private void DrawRuntimeValidationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Runtime Validation",
            EditorStyles.boldLabel
        );

        EditorGUILayout.HelpBox(
            "Multiresolution runtime validation is manual development tooling. " +
            "GPU readback and stress tests only run when explicitly requested.",
            MessageType.Info
        );

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to run multiresolution runtime validation.",
                MessageType.Warning
            );

            GUILayout.EndVertical();
            return;
        }

        TerrainHeightmapStreamer streamer =
            FindRuntimeHeightmapStreamer();

        TerrainHeightmapCacheValidator cacheValidator =
            FindRuntimeHeightmapCacheValidator();

        TerrainClipmapDisplacementValidator displacementValidator =
            FindRuntimeClipmapDisplacementValidator();

        if (
            streamer == null
            || cacheValidator == null
            || displacementValidator == null
        )
        {
            EditorGUILayout.HelpBox(
                "Required runtime validation components were not found on " +
                "WorldRoot/Clipmap.\n\nExit Play Mode and run Setup / Repair " +
                "World Hierarchy.",
                MessageType.Warning
            );

            GUILayout.EndVertical();
            return;
        }

        bool anyValidationRunning =
            cacheValidator.IsValidating
            || displacementValidator.IsValidating;

        DrawRuntimeValidationAction(
            "Height Cache Validation",
            cacheValidator.CacheValidationStatus,
            cacheValidator.CacheValidationSummary,
            "Validate All Height LOD Caches",
            anyValidationRunning,
            cacheValidator.BeginValidation
        );

        DrawWorkspaceSectionGap();

        DrawRuntimeValidationAction(
            "Cross-Resolution Validation",
            cacheValidator.CrossResolutionValidationStatus,
            cacheValidator.CrossResolutionValidationSummary,
            "Validate Cross-Resolution Height Consistency",
            anyValidationRunning,
            cacheValidator.BeginCrossResolutionValidation
        );

        DrawWorkspaceSectionGap();

        DrawRuntimeValidationAction(
            "Renderer / Displacement / Stitch Validation",
            displacementValidator.MultiresolutionValidationStatus,
            displacementValidator.MultiresolutionValidationSummary,
            "Validate Renderer Bindings & Displacement",
            anyValidationRunning,
            displacementValidator.BeginMultiresolutionValidation
        );

        DrawWorkspaceSectionGap();

        EditorGUILayout.HelpBox(
            "Independent-anchor stress temporarily moves the runtime clipmap " +
            "and cache requests without moving the Player/streaming source. " +
            "The gameplay layout is restored afterward.",
            MessageType.None
        );

        DrawRuntimeValidationAction(
            "Independent Anchor Stress",
            displacementValidator.IndependentAnchorValidationStatus,
            displacementValidator.IndependentAnchorValidationSummary,
            "Run Independent-Anchor Stress Validation",
            anyValidationRunning,
            displacementValidator.BeginIndependentAnchorStressValidation
        );

        DrawWorkspaceSectionGap();

        EditorGUILayout.HelpBox(
            "Scheduler stress submits deterministic nearby, rapid, and distant " +
            "layout requests. It verifies bounded concurrency and scheduler " +
            "diagnostic invariants, then restores the gameplay layout.",
            MessageType.None
        );

        DrawRuntimeValidationAction(
            "Height Scheduler Stress",
            displacementValidator.SchedulerStressValidationStatus,
            displacementValidator.SchedulerStressValidationSummary,
            "Run Height Scheduler Stress Test",
            anyValidationRunning,
            displacementValidator.BeginSchedulerStressValidation
        );

        DrawWorkspaceSectionGap();

        DrawRuntimeStressCertification(
            streamer,
            displacementValidator,
            anyValidationRunning
        );

        GUILayout.EndVertical();
    }

    private void DrawRuntimeValidationAction(
        string title,
        TerrainRuntimeValidationStatus status,
        string summary,
        string buttonLabel,
        bool anyValidationRunning,
        System.Action action
    )
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox
        );

        GUILayout.Label(
            title,
            EditorStyles.boldLabel
        );

        EditorGUILayout.LabelField(
            "Status",
            status.ToString()
        );

        if (!string.IsNullOrEmpty(summary))
        {
            EditorGUILayout.HelpBox(
                summary,
                MessageTypeForRuntimeValidationStatus(status)
            );
        }

        EditorGUI.BeginDisabledGroup(
            anyValidationRunning
        );

        if (
            GUILayout.Button(
                buttonLabel,
                GUILayout.ExpandWidth(true)
            )
        )
        {
            action?.Invoke();
            Repaint();
        }

        EditorGUI.EndDisabledGroup();
        GUILayout.EndVertical();
    }

    private static MessageType MessageTypeForRuntimeValidationStatus(
        TerrainRuntimeValidationStatus status
    )
    {
        switch (status)
        {
            case TerrainRuntimeValidationStatus.Failed:
                return MessageType.Error;

            case TerrainRuntimeValidationStatus.Running:
                return MessageType.Info;

            case TerrainRuntimeValidationStatus.Passed:
                return MessageType.Info;

            case TerrainRuntimeValidationStatus.Inconclusive:
                return MessageType.Warning;

            default:
                return MessageType.None;
        }
    }

    private TerrainHeightmapCacheValidator
        FindRuntimeHeightmapCacheValidator()
    {
        Transform clipmapRoot =
            FindRuntimeClipmapRoot();

        return
            clipmapRoot != null
                ? clipmapRoot.GetComponent<TerrainHeightmapCacheValidator>()
                : null;
    }

    private TerrainClipmapDisplacementValidator
        FindRuntimeClipmapDisplacementValidator()
    {
        Transform clipmapRoot =
            FindRuntimeClipmapRoot();

        return
            clipmapRoot != null
                ? clipmapRoot.GetComponent<TerrainClipmapDisplacementValidator>()
                : null;
    }
}
