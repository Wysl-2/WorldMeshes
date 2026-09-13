using UnityEditor;
using UnityEngine;

public partial class WorldMeshesEditorWindow :
    EditorWindow
{
    private void DrawAuthoringChangePipelineSettings()
    {
        GUILayout.BeginVertical(
            EditorStyles.helpBox,
            GUILayout.ExpandWidth(true)
        );

        GUILayout.Label(
            "Authoring Change + Dirty Region Pipeline",
            EditorStyles.boldLabel
        );

        bool validationBusy =
            TerrainAuthoringModifierServiceValidationUtility
                .IsRunning;

        bool validationScheduled =
            TerrainAuthoringModifierServiceValidationUtility
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

        TerrainAuthoringModifierMutationDiagnostics diagnostics =
            TerrainAuthoringModifierService
                .LastMutationDiagnostics;

        if (diagnostics != null)
        {
            EditorGUILayout.LabelField(
                "Last Operation",
                diagnostics.Operation
            );

            EditorGUILayout.LabelField(
                "Last Dirty Tiles",
                diagnostics
                    .DirtyTileCount
                    .ToString()
            );

            EditorGUILayout.LabelField(
                "Preview Notification",
                diagnostics
                    .PreviewNotificationMode
            );

            EditorGUILayout.LabelField(
                "Revision",
                $"{diagnostics.RevisionBefore} -> " +
                $"{diagnostics.RevisionAfter}"
            );
        }

        EditorGUILayout.LabelField(
            "Last Undo/Redo Dirty Tiles",
            TerrainAuthoringModifierChangeTracker
                .LastUndoRedoDirtyTileCount
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
                "Validate Authoring Change Pipeline",
                GUILayout.ExpandWidth(true)
            )
        )
        {
            /*
             * Request only. The validator begins from
             * EditorApplication.delayCall after this OnGUI event has
             * finished and all IMGUI layout groups are balanced.
             */
            TerrainAuthoringModifierServiceValidationUtility
                .RequestValidation();
        }

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(
            5f
        );

        EditorGUILayout.HelpBox(
            "Stage 12 centralizes modifier mutations, Undo/Redo, " +
            "authoringRevision updates, modifier snapshots, and " +
            "old/new dirty-tile invalidation.\n\n" +

            "Validation is deferred until after the current IMGUI " +
            "event so AssetDatabase and Undo operations cannot disrupt " +
            "the active WorldMeshes layout stack.\n\n" +

            "Terrain is not visually composed from modifiers yet. " +
            "Until Stage 13, dirty composite updates still restore " +
            "the committed base into the affected preview slices.",
            MessageType.Info
        );

        GUILayout.EndVertical();
    }
}
