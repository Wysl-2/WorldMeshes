using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawGpuCompositorFoundationSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "GPU Compositor Foundation",
            EditorStyles.boldLabel
        );

        bool validationBusy =
            TerrainHeightCompositorValidationUtility
                .IsRunning;

        bool validationScheduled =
            TerrainHeightCompositorValidationUtility
                .IsScheduled;

        EditorGUILayout.LabelField(
            "Validation",
            validationScheduled
                ? "Scheduled"
                :
                validationBusy
                    ? "Running"
                    : "Ready"
        );

        EditorGUILayout.LabelField(
            "Compute Shader",
            TerrainAuthoringPreviewService
                .HeightCompositorPrepared
                ? "Prepared"
                : "Not Prepared"
        );

        EditorGUILayout.LabelField(
            "Compute Support",
            SystemInfo.supportsComputeShaders
                ? "Supported"
                : "Unsupported"
        );

        EditorGUILayout.LabelField(
            "Last Composite Dispatch Tiles",
            TerrainAuthoringPreviewService
                .LastCompositeDispatchTileCount
                .ToString()
        );

        EditorGUILayout.LabelField(
            "Total Composite Dispatch Tiles",
            TerrainAuthoringPreviewService
                .TotalCompositeDispatchTileCount
                .ToString()
        );

        GUILayout.Space(
            5f
        );

        EditorGUI.BeginDisabledGroup(
            validationBusy
            ||
            Application.isPlaying
            ||
            EditorApplication
                .isPlayingOrWillChangePlaymode
        );

        if (
            GUILayout.Button(
                "Validate GPU Compositor Foundation",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            TerrainHeightCompositorValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Stage 13A establishes the GPU composition transaction " +
            "without applying a real terrain modifier yet.\n\n" +

            "Dirty preview slices are reset from committed base, then " +
            "passed through IdentityComposite in the existing RFloat " +
            "texture array. The RenderTexture remains alive and " +
            "terrain renderers remain bound.\n\n" +

            "The radial test stamp is intentionally not used until " +
            "Stage 13B.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
